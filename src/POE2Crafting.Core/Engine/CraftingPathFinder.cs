using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Computes known crafting strategies to reach a target item specification.
/// V1: deterministic templates with probability estimation from the mod pool.
/// </summary>
public sealed class CraftingPathFinder
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly SimAssumptions _assumptions;

    public CraftingPathFinder(GameData data, ModPool pool)
    {
        _data = data;
        _pool = pool;
        _assumptions = data.Config.Assumptions;
    }

    /// <summary>Find all applicable crafting strategies for the target item.</summary>
    public List<CraftingStrategy> FindPaths(TargetItemSpec target)
    {
        var strategies = new List<CraftingStrategy>();
        var baseItem = target.ToBaseItem(_data);
        if (baseItem.Base == null) return strategies;

        ResolveTargetMods(target, baseItem);
        RecomputeDisplayTiers(target, baseItem);

        int total = target.TotalMods;
        if (total == 0) return strategies;

        if (target.TargetRarity == Rarity.Magic)
        {
            // Magic-specific strategies (max 1 prefix + 1 suffix)
            if (total == 1)
            {
                var s = BuildTransmuteSpamPath(target, baseItem);
                if (s.OverallProbability > 0) strategies.Add(s);
            }
            else if (total == 2 && target.PrefixCount == 1 && target.SuffixCount == 1)
            {
                // Two variants: transmute for each mod, augment the other
                var s1 = BuildTransmuteAugPath(target, baseItem, 0);
                if (s1.OverallProbability > 0) strategies.Add(s1);
                var s2 = BuildTransmuteAugPath(target, baseItem, 1);
                if (s2.OverallProbability > 0) strategies.Add(s2);
            }
        }
        else
        {
            // Rare strategies (default)
            // Strategy 1: Transmute → Aug → Regal → Exalt (methodical, up to 4 mods)
            if (total >= 1 && total <= 4)
            {
                var s = BuildTransmuteRegalPath(target, baseItem);
                if (s.OverallProbability > 0) strategies.Add(s);
            }

            // Strategy 2: Alt spam → Aug → Regal (target rarest mod on magic first)
            if (total >= 2 && total <= 4)
            {
                var s = BuildAltAugRegalPath(target, baseItem);
                if (s.OverallProbability > 0) strategies.Add(s);
            }

            // Strategy 3: Alchemy + Annulment
            if (total >= 2 && total <= 4)
            {
                var s = BuildAlchemyPath(target, baseItem);
                if (s.OverallProbability > 0) strategies.Add(s);
            }

            // Strategy 4: Chaos Spam
            if (total >= 3 && total <= 6)
            {
                var s = BuildChaosSpamPath(target, baseItem);
                if (s.OverallProbability > 0) strategies.Add(s);
            }
        }

        foreach (var s in strategies) ApplyBestVariants(s);
        strategies.Sort((a, b) => b.OverallProbability.CompareTo(a.OverallProbability));
        return strategies;
    }

    /// <summary>Find strategies starting from an existing intermediate item (from the simulator).</summary>
    public List<CraftingStrategy> FindPathsFromItem(Item currentItem, TargetItemSpec target)
    {
        var strategies = new List<CraftingStrategy>();
        if (currentItem.Base == null) return strategies;

        ResolveTargetMods(target, currentItem);
        RecomputeDisplayTiers(target, currentItem);

        // Figure out which target mods are already present
        var missing = target.TargetMods.Where(tm =>
            tm.ResolvedMod != null && !currentItem.HasFamily(tm.ResolvedMod.Family)).ToList();

        if (missing.Count == 0)
        {
            strategies.Add(new CraftingStrategy
            {
                Id = "already-done",
                Name = "Target already reached!",
                Description = "All target mods are already present on the item.",
                OverallProbability = 1.0,
            });
            return strategies;
        }

        // Exalt path: if item is rare with free slots, exalt missing mods
        if (currentItem.Rarity == Rarity.Rare)
        {
            int freeP = MaxPrefixes(Rarity.Rare) - currentItem.PrefixCount;
            int freeS = MaxSuffixes(Rarity.Rare) - currentItem.SuffixCount;
            int missingP = missing.Count(m => m.AffixType == AffixType.Prefix);
            int missingS = missing.Count(m => m.AffixType == AffixType.Suffix);

            if (missingP <= freeP && missingS <= freeS)
            {
                var s = BuildExaltCompletionPath(currentItem, missing);
                if (s.OverallProbability > 0) strategies.Add(s);
            }

            // Annul + Exalt: remove unwanted, add wanted
            if (missing.Count <= 3)
            {
                var s = BuildAnnulExaltPath(currentItem, target, missing);
                if (s != null && s.OverallProbability > 0) strategies.Add(s);
            }
        }

        // Regal path if magic item targeting rare
        if (currentItem.Rarity == Rarity.Magic && missing.Count >= 1 && target.TargetRarity != Rarity.Magic)
        {
            var s = BuildRegalCompletionPath(currentItem, missing);
            if (s.OverallProbability > 0) strategies.Add(s);
        }

        // Augment path if magic item targeting magic
        if (currentItem.Rarity == Rarity.Magic && target.TargetRarity == Rarity.Magic && missing.Count == 1)
        {
            var missingMod = missing[0];
            bool hasFreeSlot = (missingMod.AffixType == AffixType.Prefix && currentItem.PrefixCount < 1) ||
                               (missingMod.AffixType == AffixType.Suffix && currentItem.SuffixCount < 1);
            if (hasFreeSlot)
            {
                var s = BuildAugmentCompletionPath(currentItem, missing);
                if (s.OverallProbability > 0) strategies.Add(s);
            }
        }

        foreach (var s in strategies) ApplyBestVariants(s);
        strategies.Sort((a, b) => b.OverallProbability.CompareTo(a.OverallProbability));
        return strategies;
    }

    // ------------------------------------------------------------------ resolve

    private void ResolveTargetMods(TargetItemSpec target, Item baseItem)
    {
        var allMods = _pool.AllForBase(baseItem).ToList();
        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod != null) continue;
            var candidates = allMods.Where(m => m.Family == tm.Family && m.AffixType == tm.AffixType);
            tm.ResolvedMod = tm.Tier != null
                ? candidates.FirstOrDefault(m => m.Tier == tm.Tier)
                : candidates.OrderBy(m => m.Tier).FirstOrDefault();
        }
    }

    /// <summary>Recompute per-page display tiers for all target mods, independent of the UI's cached values.</summary>
    private void RecomputeDisplayTiers(TargetItemSpec target, Item baseItem)
    {
        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod != null)
                tm.DisplayTier = _pool.ComputeDisplayTier(tm.ResolvedMod, baseItem);
        }
    }

    // ------------------------------------------------------------------ strategy builders

    private CraftingStrategy BuildTransmuteRegalPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "transmute-regal",
            Name = "Transmute → Augment → Regal → Exalt",
            Description = "Add one mod at a time with full control. Scour and retry on failure at each step.",
        };

        var virtualItem = baseItem.Clone();
        double cumulativeProb = 1.0;
        var orderedMods = OrderTargetMods(target.TargetMods, virtualItem);
        int stepNum = 0;

        foreach (var tm in orderedMods)
        {
            if (tm.ResolvedMod == null) continue;
            stepNum++;

            string currency;
            string restartId;

            if (virtualItem.Rarity == Rarity.Normal)
            {
                currency = "Orb of Transmutation";
                restartId = "start";
            }
            else if (virtualItem.Rarity == Rarity.Magic && virtualItem.AffixCount < 2)
            {
                currency = "Orb of Augmentation";
                restartId = "start";
            }
            else if (virtualItem.Rarity == Rarity.Magic)
            {
                currency = "Regal Orb";
                restartId = "start";
            }
            else
            {
                currency = "Exalted Orb";
                restartId = strategy.Steps.Count > 0 ? strategy.Steps[^1].Id : "start";
            }

            double prob = CalculateModProbability(virtualItem, tm, currency);
            cumulativeProb *= prob;

            var step = new CraftStep
            {
                Id = $"step-{stepNum}",
                CurrencyName = currency,
                Description = $"{currency} → {tm.DisplayName}",
                SuccessProbability = prob,
                RestartFromStepId = restartId,
                RestartLabel = restartId == "start" ? "Scour & restart" : "Annul & retry",
                Type = CraftStepType.Normal,
                Notes = new() { $"Hit chance: {prob:P2} (1 in {(prob > 0 ? Math.Ceiling(1.0 / prob) : double.PositiveInfinity):N0})" }
            };
            AttachVariants(step, virtualItem, tm, currency);
            strategy.Steps.Add(step);

            SimulateAddMod(virtualItem, tm, currency);
        }

        strategy.OverallProbability = cumulativeProb;
        return strategy;
    }

    private CraftingStrategy BuildAltAugRegalPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "alt-aug-regal",
            Name = "Transmute Spam → Aug → Regal → Exalt",
            Description = "Spam Transmutation for the rarest mod, augment a complementary mod, then regal + exalt for remaining.",
        };

        var virtualItem = baseItem.Clone();
        var orderedMods = OrderTargetMods(target.TargetMods, virtualItem);
        if (orderedMods.Count == 0) { strategy.OverallProbability = 0; return strategy; }

        double cumulativeProb = 1.0;
        int stepNum = 0;

        // Step 1: Transmute spam for the rarest mod
        var primary = orderedMods[0];
        double transmuteProb = CalculateModProbability(virtualItem, primary, "Orb of Transmutation");
        stepNum++;
        var transStep = new CraftStep
        {
            Id = $"step-{stepNum}",
            CurrencyName = "Orb of Transmutation",
            Description = $"Transmute spam → {primary.DisplayName}",
            SuccessProbability = transmuteProb,
            RestartFromStepId = "start",
            RestartLabel = "Scour & retry",
            Type = CraftStepType.Normal,
            Notes = new() { $"Hit chance: {transmuteProb:P2}, expected ~{(transmuteProb > 0 ? Math.Ceiling(1.0 / transmuteProb) : 0):N0} attempts" }
        };
        AttachVariants(transStep, virtualItem, primary, "Orb of Transmutation");
        strategy.Steps.Add(transStep);
        SimulateAddMod(virtualItem, primary, "Orb of Transmutation");
        cumulativeProb *= transmuteProb;

        // Step 2: Augment (if second mod fits on magic)
        if (orderedMods.Count >= 2)
        {
            var second = orderedMods[1];
            if (CanFitOnMagic(primary, second))
            {
                double augProb = CalculateModProbability(virtualItem, second, "Orb of Augmentation");
                stepNum++;
                var augStep = new CraftStep
                {
                    Id = $"step-{stepNum}",
                    CurrencyName = "Orb of Augmentation",
                    Description = $"Augment → {second.DisplayName}",
                    SuccessProbability = augProb,
                    RestartFromStepId = "start",
                    RestartLabel = "Scour & restart",
                    Type = CraftStepType.Normal,
                    Notes = new() { $"Hit chance: {augProb:P2}" }
                };
                AttachVariants(augStep, virtualItem, second, "Orb of Augmentation");
                strategy.Steps.Add(augStep);
                SimulateAddMod(virtualItem, second, "Orb of Augmentation");
                cumulativeProb *= augProb;
            }
        }

        // Step 3+: Regal then Exalts for remaining
        for (int i = strategy.Steps.Count; i < orderedMods.Count; i++)
        {
            var tm = orderedMods[i];
            string currency = virtualItem.Rarity == Rarity.Magic ? "Regal Orb" : "Exalted Orb";
            double prob = CalculateModProbability(virtualItem, tm, currency);
            stepNum++;

            var regalExaltStep = new CraftStep
            {
                Id = $"step-{stepNum}",
                CurrencyName = currency,
                Description = $"{currency} → {tm.DisplayName}",
                SuccessProbability = prob,
                RestartFromStepId = currency == "Regal Orb" ? "start" : strategy.Steps[^1].Id,
                RestartLabel = currency == "Regal Orb" ? "Scour & restart" : "Annul & retry",
                Type = CraftStepType.Normal,
                Notes = new() { $"Hit chance: {prob:P2}" }
            };
            AttachVariants(regalExaltStep, virtualItem, tm, currency);
            strategy.Steps.Add(regalExaltStep);
            SimulateAddMod(virtualItem, tm, currency);
            cumulativeProb *= prob;
        }

        strategy.OverallProbability = cumulativeProb;
        return strategy;
    }

    private CraftingStrategy BuildAlchemyPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "alchemy",
            Name = "Alchemy + Annulment",
            Description = "Orb of Alchemy adds ~4 mods at once, then annul unwanted mods. Good when target mods are common.",
        };

        var virtualItem = baseItem.Clone();
        double alchProb = EstimateAlchemyHitProbability(target, virtualItem);

        strategy.Steps.Add(new CraftStep
        {
            Id = "step-alch",
            CurrencyName = "Orb of Alchemy",
            Description = $"Alchemy → hit all {target.TotalMods} target mods (in {_assumptions.AlchemyModCount} slots)",
            SuccessProbability = alchProb,
            RestartFromStepId = "start",
            RestartLabel = "Scour & retry",
            Type = CraftStepType.Normal,
            Notes = new()
            {
                $"Chance all target mods in one alchemy: {alchProb:P4}",
                $"Expected attempts: ~{(alchProb > 0 ? Math.Ceiling(1.0 / alchProb) : double.PositiveInfinity):N0}"
            }
        });

        double cumulativeProb = alchProb;

        // Annul unwanted mods
        int unwanted = _assumptions.AlchemyModCount - target.TotalMods;
        for (int i = 0; i < unwanted; i++)
        {
            int remaining = _assumptions.AlchemyModCount - i;
            int wantedRemaining = target.TotalMods;
            double hitUnwanted = (double)(remaining - wantedRemaining) / remaining;
            double brickChance = 1.0 - hitUnwanted;

            strategy.Steps.Add(new CraftStep
            {
                Id = $"step-annul-{i + 1}",
                CurrencyName = "Orb of Annulment",
                Description = $"Annul ({remaining - wantedRemaining}/{remaining} safe)",
                SuccessProbability = hitUnwanted,
                BrickProbability = brickChance,
                RestartFromStepId = "step-alch",
                RestartLabel = "Lost a wanted mod → re-alchemy",
                Type = brickChance > 0.3 ? CraftStepType.Brick : CraftStepType.Normal,
                Notes = new() { $"Risk of removing a wanted mod: {brickChance:P1}" }
            });
            cumulativeProb *= hitUnwanted;
        }

        strategy.HasBrickRisk = strategy.Steps.Any(s => s.Type == CraftStepType.Brick);
        strategy.OverallProbability = cumulativeProb;
        strategy.Warnings.Add($"Alchemy adds {_assumptions.AlchemyModCount} mods (configurable assumption). Actual count may vary.");
        return strategy;
    }

    private CraftingStrategy BuildChaosSpamPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "chaos-spam",
            Name = "Chaos Orb Spam",
            Description = "Brute-force rerolling. Each Chaos removes one mod and adds one. Extremely low per-attempt success rate.",
        };

        var virtualItem = baseItem.Clone();
        virtualItem.Rarity = Rarity.Rare;
        double chaosProb = EstimateAlchemyHitProbability(target, virtualItem);

        strategy.Steps = new List<CraftStep>
        {
            new CraftStep
            {
                Id = "step-setup",
                CurrencyName = "Orb of Alchemy",
                Description = "Start: Alchemy on Normal base",
                SuccessProbability = 1.0,
                Type = CraftStepType.Checkpoint,
            },
            new CraftStep
            {
                Id = "step-chaos",
                CurrencyName = "Chaos Orb",
                Description = $"Chaos spam until all {target.TotalMods} target mods hit",
                SuccessProbability = chaosProb,
                RestartFromStepId = "step-chaos",
                RestartLabel = "Keep spamming",
                Type = CraftStepType.Normal,
                Notes = new()
                {
                    "Each chaos: remove 1 random mod, add 1 random mod",
                    $"Per-attempt estimate: {chaosProb:P6}",
                    $"Expected: ~{(chaosProb > 0 ? Math.Ceiling(1.0 / chaosProb) : double.PositiveInfinity):N0} chaos orbs",
                    "Note: this is a rough estimate. Chaos doesn't reroll the whole item."
                }
            }
        };

        strategy.OverallProbability = chaosProb;
        strategy.Warnings.Add("Chaos spam probability is a rough estimate. Actual results depend on item state.");
        return strategy;
    }

    private CraftingStrategy BuildExaltCompletionPath(Item currentItem, List<TargetMod> missing)
    {
        var strategy = new CraftingStrategy
        {
            Id = "exalt-completion",
            Name = "Exalt Completion",
            Description = "Item has free slots. Exalt the missing mods directly.",
        };

        var virtualItem = currentItem.Clone();
        double cumulativeProb = 1.0;

        for (int i = 0; i < missing.Count; i++)
        {
            var tm = missing[i];
            double prob = CalculateModProbability(virtualItem, tm, "Exalted Orb");
            cumulativeProb *= prob;

            var exStep = new CraftStep
            {
                Id = $"step-exalt-{i + 1}",
                CurrencyName = "Exalted Orb",
                Description = $"Exalt → {tm.DisplayName}",
                SuccessProbability = prob,
                RestartFromStepId = i > 0 ? $"step-exalt-{i}" : "start",
                RestartLabel = "Annul & retry",
                Type = CraftStepType.Normal,
                Notes = new() { $"Hit chance: {prob:P2}" }
            };
            AttachVariants(exStep, virtualItem, tm, "Exalted Orb");
            strategy.Steps.Add(exStep);

            SimulateAddMod(virtualItem, tm, "Exalted Orb");
        }

        strategy.OverallProbability = cumulativeProb;
        return strategy;
    }

    private CraftingStrategy? BuildAnnulExaltPath(Item currentItem, TargetItemSpec target, List<TargetMod> missing)
    {
        // Count unwanted mods that could be annulled
        var wanted = target.TargetMods
            .Where(tm => tm.ResolvedMod != null && currentItem.HasFamily(tm.ResolvedMod.Family))
            .ToList();
        int unwantedCount = currentItem.AffixCount - wanted.Count;

        if (unwantedCount <= 0 || missing.Count == 0) return null;

        var strategy = new CraftingStrategy
        {
            Id = "annul-exalt",
            Name = "Annul + Exalt",
            Description = "Remove unwanted mods with Annulment, then Exalt missing target mods.",
            HasBrickRisk = true,
        };

        double cumulativeProb = 1.0;
        var virtualItem = currentItem.Clone();

        // Annul steps
        for (int i = 0; i < Math.Min(unwantedCount, missing.Count); i++)
        {
            int totalOnItem = virtualItem.AffixCount;
            int wantedOnItem = wanted.Count;
            double hitUnwanted = (double)(totalOnItem - wantedOnItem) / totalOnItem;

            strategy.Steps.Add(new CraftStep
            {
                Id = $"step-annul-{i + 1}",
                CurrencyName = "Orb of Annulment",
                Description = $"Annul unwanted ({totalOnItem - wantedOnItem}/{totalOnItem} safe)",
                SuccessProbability = hitUnwanted,
                BrickProbability = 1.0 - hitUnwanted,
                RestartFromStepId = "start",
                RestartLabel = "Lost a wanted mod → start over",
                Type = CraftStepType.Brick,
                Notes = new() { $"Risk of removing a wanted mod: {1.0 - hitUnwanted:P1}" }
            });
            cumulativeProb *= hitUnwanted;

            // Simulate removal of one unwanted mod
            var unwantedMod = virtualItem.Mods.FirstOrDefault(m =>
                Item.IsAffixKind(m.Kind) && !m.Fractured &&
                !wanted.Any(w => w.ResolvedMod?.Family == m.Def?.Family));
            if (unwantedMod != null) virtualItem.Mods.Remove(unwantedMod);
        }

        // Exalt steps
        for (int i = 0; i < missing.Count; i++)
        {
            var tm = missing[i];
            double prob = CalculateModProbability(virtualItem, tm, "Exalted Orb");
            cumulativeProb *= prob;

            var exStep = new CraftStep
            {
                Id = $"step-exalt-{i + 1}",
                CurrencyName = "Exalted Orb",
                Description = $"Exalt → {tm.DisplayName}",
                SuccessProbability = prob,
                RestartFromStepId = strategy.Steps[^1].Id,
                RestartLabel = "Annul & retry",
                Type = CraftStepType.Normal,
                Notes = new() { $"Hit chance: {prob:P2}" }
            };
            AttachVariants(exStep, virtualItem, tm, "Exalted Orb");
            strategy.Steps.Add(exStep);

            SimulateAddMod(virtualItem, tm, "Exalted Orb");
        }

        strategy.OverallProbability = cumulativeProb;
        return strategy;
    }

    private CraftingStrategy BuildRegalCompletionPath(Item currentItem, List<TargetMod> missing)
    {
        var strategy = new CraftingStrategy
        {
            Id = "regal-completion",
            Name = "Regal → Exalt Completion",
            Description = "Upgrade magic item to rare with Regal, then Exalt remaining.",
        };

        var virtualItem = currentItem.Clone();
        double cumulativeProb = 1.0;
        int stepNum = 0;

        foreach (var tm in missing)
        {
            stepNum++;
            string currency = virtualItem.Rarity == Rarity.Magic ? "Regal Orb" : "Exalted Orb";
            double prob = CalculateModProbability(virtualItem, tm, currency);
            cumulativeProb *= prob;

            var rStep = new CraftStep
            {
                Id = $"step-{stepNum}",
                CurrencyName = currency,
                Description = $"{currency} → {tm.DisplayName}",
                SuccessProbability = prob,
                RestartFromStepId = stepNum > 1 ? $"step-{stepNum - 1}" : "start",
                RestartLabel = "Retry",
                Type = CraftStepType.Normal,
                Notes = new() { $"Hit chance: {prob:P2}" }
            };
            AttachVariants(rStep, virtualItem, tm, currency);
            strategy.Steps.Add(rStep);

            SimulateAddMod(virtualItem, tm, currency);
        }

        strategy.OverallProbability = cumulativeProb;
        return strategy;
    }

    // ------------------------------------------------------------------ magic-target strategies

    private CraftingStrategy BuildTransmuteSpamPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "transmute-spam",
            Name = "Transmutation Spam",
            Description = "Spam Transmutation on the Normal base until the target mod hits. Scour on miss and retry.",
        };

        var mod = target.TargetMods[0];
        if (mod.ResolvedMod == null) { strategy.OverallProbability = 0; return strategy; }

        var virtualItem = baseItem.Clone();
        double prob = CalculateModProbability(virtualItem, mod, "Orb of Transmutation");

        var tStep = new CraftStep
        {
            Id = "step-transmute",
            CurrencyName = "Orb of Transmutation",
            Description = $"Transmute → {mod.DisplayName}",
            SuccessProbability = prob,
            RestartFromStepId = "start",
            RestartLabel = "Scour & retry",
            Type = CraftStepType.Normal,
            Notes = new()
            {
                $"Hit chance per transmute: {prob:P2} (1 in {(prob > 0 ? Math.Ceiling(1.0 / prob) : double.PositiveInfinity):N0})",
                "Transmutation adds 1 mod. If target not hit, Scour and retry."
            }
        };
        AttachVariants(tStep, virtualItem, mod, "Orb of Transmutation");
        strategy.Steps.Add(tStep);

        strategy.OverallProbability = prob;
        return strategy;
    }

    private CraftingStrategy BuildTransmuteAugPath(TargetItemSpec target, Item baseItem, int primaryIndex)
    {
        var mods = target.TargetMods;
        var primary = mods[primaryIndex];
        var secondary = mods[1 - primaryIndex];

        if (primary.ResolvedMod == null || secondary.ResolvedMod == null)
            return new CraftingStrategy { Id = $"transmute-aug-{primaryIndex}", OverallProbability = 0 };

        var strategy = new CraftingStrategy
        {
            Id = $"transmute-aug-{primaryIndex}",
            Name = $"Transmute ({primary.ResolvedMod.Name}) → Augment",
            Description = $"Transmute for {primary.DisplayName}, then Augment for {secondary.DisplayName}.",
        };

        var virtualItem = baseItem.Clone();
        double transProb = CalculateModProbability(virtualItem, primary, "Orb of Transmutation");

        var tStep = new CraftStep
        {
            Id = "step-transmute",
            CurrencyName = "Orb of Transmutation",
            Description = $"Transmute → {primary.DisplayName}",
            SuccessProbability = transProb,
            RestartFromStepId = "start",
            RestartLabel = "Scour & retry",
            Type = CraftStepType.Normal,
            Notes = new() { $"Hit chance: {transProb:P2} (1 in {(transProb > 0 ? Math.Ceiling(1.0 / transProb) : double.PositiveInfinity):N0})" }
        };
        AttachVariants(tStep, virtualItem, primary, "Orb of Transmutation");
        strategy.Steps.Add(tStep);

        SimulateAddMod(virtualItem, primary, "Orb of Transmutation");

        double augProb = CalculateModProbability(virtualItem, secondary, "Orb of Augmentation");

        var aStep = new CraftStep
        {
            Id = "step-augment",
            CurrencyName = "Orb of Augmentation",
            Description = $"Augment → {secondary.DisplayName}",
            SuccessProbability = augProb,
            RestartFromStepId = "start",
            RestartLabel = "Scour & restart",
            Type = CraftStepType.Normal,
            Notes = new() { $"Hit chance: {augProb:P2} (1 in {(augProb > 0 ? Math.Ceiling(1.0 / augProb) : double.PositiveInfinity):N0})" }
        };
        AttachVariants(aStep, virtualItem, secondary, "Orb of Augmentation");
        strategy.Steps.Add(aStep);

        strategy.OverallProbability = transProb * augProb;
        return strategy;
    }

    private CraftingStrategy BuildAugmentCompletionPath(Item currentItem, List<TargetMod> missing)
    {
        var strategy = new CraftingStrategy
        {
            Id = "augment-completion",
            Name = "Augment Completion",
            Description = "Magic item has a free slot. Augment the missing mod directly.",
        };

        var virtualItem = currentItem.Clone();
        var tm = missing[0];
        double prob = CalculateModProbability(virtualItem, tm, "Orb of Augmentation");

        var augStep = new CraftStep
        {
            Id = "step-aug",
            CurrencyName = "Orb of Augmentation",
            Description = $"Augment → {tm.DisplayName}",
            SuccessProbability = prob,
            RestartFromStepId = "start",
            RestartLabel = "Annul & retry",
            Type = CraftStepType.Normal,
            Notes = new() { $"Hit chance: {prob:P2} (1 in {(prob > 0 ? Math.Ceiling(1.0 / prob) : double.PositiveInfinity):N0})" }
        };
        AttachVariants(augStep, virtualItem, tm, "Orb of Augmentation");
        strategy.Steps.Add(augStep);

        strategy.OverallProbability = prob;
        return strategy;
    }

    // ------------------------------------------------------------------ probability calculations

    private double CalculateModProbability(Item virtualItem, TargetMod target, string currencyName)
        => CalculateModProbabilityWithLevel(virtualItem, target, currencyName, 0);

    private double CalculateModProbabilityWithLevel(Item virtualItem, TargetMod target, string currencyName, int minModLevel)
    {
        if (target.ResolvedMod == null) return 0;

        var targetRarity = currencyName switch
        {
            "Orb of Transmutation" or "Orb of Augmentation" or
            "Greater Orb of Transmutation" or "Greater Orb of Augmentation" or
            "Perfect Orb of Transmutation" or "Perfect Orb of Augmentation" => Rarity.Magic,
            _ => Rarity.Rare
        };

        var prefixCands = (virtualItem.PrefixCount < MaxPrefixes(targetRarity))
            ? _pool.Candidates(virtualItem, AffixType.Prefix, minModLevel) : new();
        var suffixCands = (virtualItem.SuffixCount < MaxSuffixes(targetRarity))
            ? _pool.Candidates(virtualItem, AffixType.Suffix, minModLevel) : new();

        double totalWeight = prefixCands.Sum(c => (double)c.Weight) + suffixCands.Sum(c => (double)c.Weight);
        if (totalWeight == 0) return 0;

        var cands = target.AffixType == AffixType.Prefix ? prefixCands : suffixCands;

        // Sum weight of all matching mods (all tiers if Tier==null, or specific tier)
        double targetWeight;
        if (target.Tier == null)
            targetWeight = cands.Where(c => c.Mod.Family == target.Family).Sum(c => (double)c.Weight);
        else
            targetWeight = cands.Where(c => c.Mod.Id == target.ResolvedMod.Id).Sum(c => (double)c.Weight);

        return targetWeight / totalWeight;
    }

    /// <summary>Get variant names for a base currency (Normal, Greater, Perfect).</summary>
    private static string[] GetVariantNames(string baseCurrency) => baseCurrency switch
    {
        "Orb of Transmutation" => new[] { "Orb of Transmutation", "Greater Orb of Transmutation", "Perfect Orb of Transmutation" },
        "Orb of Augmentation" => new[] { "Orb of Augmentation", "Greater Orb of Augmentation", "Perfect Orb of Augmentation" },
        "Exalted Orb" => new[] { "Exalted Orb", "Greater Exalted Orb", "Perfect Exalted Orb" },
        "Regal Orb" => new[] { "Regal Orb", "Greater Regal Orb", "Perfect Regal Orb" },
        _ => Array.Empty<string>()
    };

    /// <summary>Compute probabilities for each currency variant (Normal/Greater/Perfect) and mark the best one.</summary>
    private List<CurrencyVariant> ComputeVariants(Item virtualItem, TargetMod target, string baseCurrency)
    {
        var variantNames = GetVariantNames(baseCurrency);
        if (variantNames.Length == 0) return new();

        var result = new List<CurrencyVariant>();
        double bestProb = -1;
        int bestIdx = -1;

        for (int i = 0; i < variantNames.Length; i++)
        {
            var currency = _data.FindCurrency(variantNames[i]);
            int minModLevel = currency?.MinModLevel ?? 0;
            double prob = CalculateModProbabilityWithLevel(virtualItem, target, baseCurrency, minModLevel);

            result.Add(new CurrencyVariant
            {
                Name = variantNames[i],
                MinModLevel = minModLevel,
                Probability = prob,
            });

            if (prob > bestProb) { bestProb = prob; bestIdx = i; }
        }

        if (bestIdx >= 0) result[bestIdx].IsRecommended = true;
        return result;
    }

    private double EstimateAlchemyHitProbability(TargetItemSpec target, Item baseItem)
    {
        var virtualItem = baseItem.Clone();
        if (virtualItem.Rarity != Rarity.Normal) virtualItem.Rarity = Rarity.Normal;

        var prefixCands = _pool.Candidates(virtualItem, AffixType.Prefix);
        var suffixCands = _pool.Candidates(virtualItem, AffixType.Suffix);

        double totalWeight = prefixCands.Sum(c => (double)c.Weight) + suffixCands.Sum(c => (double)c.Weight);
        if (totalWeight == 0) return 0;

        int modCount = _assumptions.AlchemyModCount;
        double prob = 1.0;

        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod == null) return 0;

            var cands = tm.AffixType == AffixType.Prefix ? prefixCands : suffixCands;
            double modWeight = tm.Tier == null
                ? cands.Where(c => c.Mod.Family == tm.Family).Sum(c => (double)c.Weight)
                : cands.Where(c => c.Mod.Id == tm.ResolvedMod.Id).Sum(c => (double)c.Weight);

            if (modWeight == 0) return 0;

            double singleProb = modWeight / totalWeight;
            double appearsInAlch = 1.0 - Math.Pow(1.0 - singleProb, modCount);
            prob *= appearsInAlch;
        }

        return prob;
    }

    // ------------------------------------------------------------------ variant attachment

    /// <summary>Compute and attach currency variants to a step. Updates the step's best probability if a variant is better.</summary>
    private void AttachVariants(CraftStep step, Item virtualItem, TargetMod target, string baseCurrency)
    {
        var variants = ComputeVariants(virtualItem, target, baseCurrency);
        if (variants.Count == 0) return;
        step.Variants = variants;

        // If a non-Normal variant has better probability, add a recommendation note
        var best = variants.FirstOrDefault(v => v.IsRecommended);
        if (best != null && best.Name != baseCurrency && best.Probability > 0)
        {
            step.Notes.Add($"★ Best: {best.Name} ({best.Probability:P2})");
        }

        // Explain 0% variants: mod level is below the currency's minimum
        foreach (var v in variants)
        {
            if (v.Probability == 0 && v.MinModLevel > 0 && target.ResolvedMod != null && target.ResolvedMod.Level < v.MinModLevel)
            {
                step.Notes.Add($"{v.Name}: 0% — mod requires level {target.ResolvedMod.Level}, but this currency requires min level {v.MinModLevel}");
            }
        }
    }

    // ------------------------------------------------------------------ best-variant promotion

    /// <summary>
    /// After a strategy is built, promote each step to its best currency variant
    /// (e.g. Greater Transmutation instead of normal Transmutation) and recalculate
    /// the strategy's overall probability so sorting reflects the actual best approach.
    /// </summary>
    private static void ApplyBestVariants(CraftingStrategy strategy)
    {
        double overall = 1.0;
        foreach (var step in strategy.Steps)
        {
            if (step.Variants is { Count: > 0 })
            {
                var best = step.Variants.FirstOrDefault(v => v.IsRecommended);
                if (best != null && best.Probability > step.SuccessProbability)
                {
                    step.SuccessProbability = best.Probability;
                    step.CurrencyName = best.Name;
                    // Update hit-chance notes to reflect the better variant
                    for (int i = 0; i < step.Notes.Count; i++)
                    {
                        if (step.Notes[i].StartsWith("Hit chance"))
                            step.Notes[i] = $"Hit chance: {best.Probability:P2} (1 in {(best.Probability > 0 ? Math.Ceiling(1.0 / best.Probability) : double.PositiveInfinity):N0}) — {best.Name}";
                    }
                    step.Notes.RemoveAll(n => n.StartsWith("★ Best:"));
                }
            }
            overall *= step.SuccessProbability;
        }
        strategy.OverallProbability = overall;
    }

    // ------------------------------------------------------------------ helpers

    private List<TargetMod> OrderTargetMods(List<TargetMod> mods, Item baseItem)
    {
        return mods
            .Where(m => m.ResolvedMod != null)
            .OrderBy(m => CalculateModProbability(baseItem, m, "Exalted Orb"))
            .ToList();
    }

    private static bool CanFitOnMagic(TargetMod first, TargetMod second)
        => first.AffixType != second.AffixType;

    private void SimulateAddMod(Item virtualItem, TargetMod target, string currency)
    {
        if (target.ResolvedMod == null) return;

        if (currency == "Orb of Transmutation")
            virtualItem.Rarity = Rarity.Magic;
        else if (currency == "Regal Orb")
            virtualItem.Rarity = Rarity.Rare;

        virtualItem.Mods.Add(new ItemMod
        {
            ModId = target.ResolvedMod.Id,
            Def = target.ResolvedMod,
            Affix = target.AffixType,
            Kind = ModKind.Explicit,
        });
    }

    private int MaxPrefixes(Rarity r) => r switch { Rarity.Magic => _assumptions.MagicMaxPrefixes, Rarity.Rare => _assumptions.RareMaxPrefixes, _ => 0 };
    private int MaxSuffixes(Rarity r) => r switch { Rarity.Magic => _assumptions.MagicMaxSuffixes, Rarity.Rare => _assumptions.RareMaxSuffixes, _ => 0 };

    // ------------------------------------------------------------------ Mermaid generation

    /// <summary>Generate a Mermaid flowchart from a strategy.</summary>
    public static string ToMermaid(CraftingStrategy strategy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine("    start([\"Normal Base Item\"])");

        string prevId = "start";
        foreach (var step in strategy.Steps)
        {
            string safeDesc = EscapeMermaid(step.Description);
            string probText = step.SuccessProbability < 1.0 ? $"<br/>{step.SuccessProbability:P1}" : "";

            string nodeShape = step.Type switch
            {
                CraftStepType.Brick => $"    {step.Id}{{\"{safeDesc}{probText}\"}}",
                CraftStepType.Checkpoint => $"    {step.Id}([\"{safeDesc}\"])",
                CraftStepType.Decision => $"    {step.Id}{{{{\"{safeDesc}{probText}\"}}}}",
                _ => $"    {step.Id}[\"{safeDesc}{probText}\"]",
            };

            sb.AppendLine(nodeShape);
            sb.AppendLine($"    {prevId} -->|\"{EscapeMermaid(step.CurrencyName)}\"| {step.Id}");

            if (step.RestartFromStepId != null && step.SuccessProbability < 1.0)
            {
                double failProb = 1.0 - step.SuccessProbability;
                string failLabel = EscapeMermaid(step.RestartLabel ?? "Retry");
                sb.AppendLine($"    {step.Id} -.->|\"{failProb:P0} {failLabel}\"| {step.RestartFromStepId}");
            }

            prevId = step.Id;
        }

        sb.AppendLine($"    {prevId} --> finish([\"Target Item\"])");

        // Styling
        sb.AppendLine("    style start fill:#1a1a20,stroke:#af8f4e,color:#d4b462");
        sb.AppendLine("    style finish fill:#1a3a1a,stroke:#6aaa40,color:#6aaa40");
        foreach (var step in strategy.Steps)
        {
            var (fill, stroke) = step.Type switch
            {
                CraftStepType.Brick => ("#3a1a1a", "#cc4444"),
                CraftStepType.Checkpoint => ("#1a1a3a", "#6688cc"),
                _ => ("#1a1a20", "#af8f4e")
            };
            sb.AppendLine($"    style {step.Id} fill:{fill},stroke:{stroke},color:#c8c4b8");
        }

        return sb.ToString();
    }

    private static string EscapeMermaid(string text)
        => text.Replace("\"", "'").Replace("\n", "<br/>");
}
