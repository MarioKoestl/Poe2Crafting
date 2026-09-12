using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Computes known crafting strategies to reach a target item specification.
/// V1: deterministic templates; per-step probabilities come from the engine's addition distribution
/// (same pool, weights and prefix/suffix rule as the simulator). A target mod is hit by its tier or any better tier.
/// </summary>
public sealed class CraftingPathFinder
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly CraftingEngine _engine;
    private readonly SimAssumptions _assumptions;

    private const string Transmute = "Orb of Transmutation";
    private const string Augment = "Orb of Augmentation";
    private const string Regal = "Regal Orb";
    private const string Exalt = "Exalted Orb";
    private const string RestartLabel = "Start over with a new base";

    public CraftingPathFinder(GameData data, ModPool pool)
    {
        _data = data;
        _pool = pool;
        _engine = new CraftingEngine(data, pool);
        _assumptions = data.Config.Assumptions;
    }

    /// <summary>Find all applicable crafting strategies for the target item, starting from a Normal base.</summary>
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
            if (total == 1)
                strategies.Add(BuildTransmuteSpamPath(target, baseItem));
            else if (total == 2 && target.PrefixCount == 1 && target.SuffixCount == 1)
            {
                strategies.Add(BuildTransmuteAugPath(target, baseItem, 0));
                strategies.Add(BuildTransmuteAugPath(target, baseItem, 1));
            }
        }
        else
        {
            if (total <= 6 && target.PrefixCount <= 3 && target.SuffixCount <= 3)
                strategies.Add(BuildTransmuteRegalPath(target, baseItem));
            strategies.AddRange(BuildEssencePaths(target, baseItem));
            if (total >= 2 && total <= _assumptions.AlchemyModCount)
                strategies.Add(BuildAlchemyPath(target, baseItem));
            if (total >= 3)
                strategies.Add(BuildChaosSpamPath(target, baseItem));
        }

        return Finish(strategies);
    }

    /// <summary>Find strategies starting from an existing intermediate item (from the simulator).</summary>
    public List<CraftingStrategy> FindPathsFromItem(Item currentItem, TargetItemSpec target)
    {
        var strategies = new List<CraftingStrategy>();
        if (currentItem.Base == null) return strategies;

        ResolveTargetMods(target, currentItem);
        RecomputeDisplayTiers(target, currentItem);

        var missing = target.TargetMods.Where(tm => tm.ResolvedMod != null && !currentItem.Affixes.Any(m => tm.Matches(m.Def))).ToList();
        if (missing.Count == 0)
        {
            strategies.Add(new CraftingStrategy
            {
                Id = "already-done",
                Name = "Target already reached!",
                Description = "All target mods are already present on the item.",
                OverallProbability = 1.0,
                StartLabel = "Current Item",
            });
            return strategies;
        }

        // a family that is present with a too-low tier blocks rolling the target again
        var blocked = missing.Where(tm => currentItem.HasFamily(tm.Family)).ToList();

        if (currentItem.Rarity == Rarity.Rare)
        {
            int freeP = _assumptions.RareMaxPrefixes - currentItem.PrefixCount;
            int freeS = _assumptions.RareMaxSuffixes - currentItem.SuffixCount;
            if (blocked.Count == 0 && missing.Count(m => m.AffixType == AffixType.Prefix) <= freeP && missing.Count(m => m.AffixType == AffixType.Suffix) <= freeS)
                strategies.Add(BuildCompletionPath(currentItem, missing, "exalt-completion", "Exalt Completion", "Item has free slots. Exalt the missing mods directly."));

            if (BuildAnnulExaltPath(currentItem, target, missing) is { } annul) strategies.Add(annul);
        }

        if (currentItem.Rarity == Rarity.Magic && blocked.Count == 0)
        {
            if (target.TargetRarity == Rarity.Magic)
            {
                if (missing.Count == 1 && currentItem.CountOf(missing[0].AffixType) < 1)
                    strategies.Add(BuildCompletionPath(currentItem, missing, "augment-completion", "Augment Completion", "Magic item has a free slot. Augment the missing mod directly."));
            }
            else
                strategies.Add(BuildCompletionPath(currentItem, missing, "regal-completion", "Regal → Exalt Completion", "Upgrade the magic item to rare with Regal, then Exalt the remaining mods."));
        }

        foreach (var s in strategies) s.StartLabel = "Current Item";
        if (blocked.Count > 0)
            foreach (var s in strategies)
                s.Warnings.Add($"The item already has a lower tier of: {string.Join(", ", blocked.Select(b => b.DisplayName))}. It must be removed before the target tier can roll.");
        return Finish(strategies);
    }

    private static List<CraftingStrategy> Finish(List<CraftingStrategy> strategies)
    {
        foreach (var s in strategies) ApplyBestVariants(s);
        return strategies.Where(s => s.OverallProbability > 0)
            .OrderByDescending(s => s.OverallProbability).ToList();
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
                : candidates.OrderBy(m => m.Level).FirstOrDefault();
        }
    }

    /// <summary>Recompute per-base display tiers for all target mods, independent of the UI's cached values.</summary>
    private void RecomputeDisplayTiers(TargetItemSpec target, Item baseItem)
    {
        foreach (var tm in target.TargetMods)
            if (tm.ResolvedMod != null)
                tm.DisplayTier = _pool.DisplayTier(tm.ResolvedMod, baseItem);
    }

    // ------------------------------------------------------------------ strategy builders

    /// <summary>
    /// Transmute → Augment on the magic item (one prefix + one suffix, rarest first), then Regal and Exalts for the rest.
    /// </summary>
    private CraftingStrategy BuildTransmuteRegalPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "transmute-regal",
            Name = "Transmute → Augment → Regal → Exalt",
            Description = "Add one mod at a time with full control. The two rarest mods of different affix types go on the magic item, where a miss is cheapest.",
        };

        var virtualItem = baseItem.Clone();
        var ordered = MagicFirstOrder(target.TargetMods.Where(m => m.ResolvedMod != null).ToList(), virtualItem);
        int stepNum = 0;
        foreach (var tm in ordered)
        {
            string currency = virtualItem.Rarity switch
            {
                Rarity.Normal => Transmute,
                Rarity.Magic when virtualItem.AffixCount < 2 && virtualItem.CountOf(tm.AffixType) == 0 => Augment,
                Rarity.Magic => Regal,
                _ => Exalt,
            };
            AddStep(strategy, virtualItem, tm, currency, ++stepNum, currency == Exalt && strategy.Steps.Count > 0 ? strategy.Steps[^1].Id : "start");
        }
        return strategy;
    }

    /// <summary>
    /// Essence start: build a magic item with the other target mods, then a Lesser/Normal/Greater Essence turns it rare with a guaranteed target mod.
    /// </summary>
    private IEnumerable<CraftingStrategy> BuildEssencePaths(TargetItemSpec target, Item baseItem)
    {
        var mods = target.TargetMods.Where(m => m.ResolvedMod != null).ToList();
        foreach (var tm in mods)
        {
            var essence = _data.Essences
                .Where(e => !e.RemovesRandomModifier)
                .Select(e => (essence: e, mod: _data.EssenceModFor(e, baseItem.Base, baseItem.ItemClass)))
                .Where(x => x.mod != null && tm.Matches(x.mod))
                .OrderBy(x => x.mod!.Level)   // cheapest essence tier that is good enough
                .FirstOrDefault();
            if (essence.essence == null) continue;

            var others = mods.Where(m => m != tm).ToList();
            if (others.Count == 0) continue;

            var strategy = new CraftingStrategy
            {
                Id = $"essence-{essence.essence.Slug ?? essence.essence.Name}",
                Name = $"Transmute → {essence.essence.Name} → Exalt",
                Description = $"{essence.essence.Name} guarantees {tm.DisplayName} when it upgrades the magic item to rare.",
            };

            var virtualItem = baseItem.Clone();
            var magicPart = MagicFirstOrder(others, virtualItem).ToList();
            int stepNum = 0;
            // the magic item keeps at most one prefix and one suffix
            var onMagic = new List<TargetMod>();
            foreach (var o in magicPart)
                if (onMagic.Count < 2 && onMagic.All(x => x.AffixType != o.AffixType)) onMagic.Add(o);

            foreach (var o in onMagic)
                AddStep(strategy, virtualItem, o, virtualItem.Rarity == Rarity.Normal ? Transmute : Augment, ++stepNum, "start");

            // the essence step itself is guaranteed
            var essenceMod = essence.mod!;
            strategy.Steps.Add(new CraftStep
            {
                Id = $"step-{++stepNum}",
                CurrencyName = essence.essence.Name,
                Description = $"{essence.essence.Name} → {tm.DisplayName} (guaranteed: {essenceMod.Text})",
                SuccessProbability = 1.0,
                Type = CraftStepType.Checkpoint,
                Notes = new() { "Magic → Rare with a guaranteed modifier." },
            });
            virtualItem.Rarity = Rarity.Rare;
            virtualItem.Mods.Add(new ItemMod { ModId = essenceMod.Id, Def = essenceMod, Affix = essenceMod.AffixType, Kind = ModKind.Explicit });

            foreach (var o in others.Except(onMagic).OrderBy(o => HitProbability(virtualItem, o, Rarity.Rare)))
                AddStep(strategy, virtualItem, o, Exalt, ++stepNum, strategy.Steps[^1].Id);

            yield return strategy;
        }
    }

    private CraftingStrategy BuildAlchemyPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "alchemy",
            Name = "Alchemy + Annulment",
            Description = $"Orb of Alchemy adds {_assumptions.AlchemyModCount} mods at once, then annul the unwanted ones. Good when target mods are common.",
        };

        double alchProb = EstimateAlchemyHitProbability(target, baseItem);
        strategy.Steps.Add(new CraftStep
        {
            Id = "step-alch",
            CurrencyName = "Orb of Alchemy",
            Description = $"Alchemy → hit all {target.TotalMods} target mods (in {_assumptions.AlchemyModCount} slots)",
            SuccessProbability = alchProb,
            RestartFromStepId = "start",
            RestartLabel = RestartLabel,
            Notes = new()
            {
                $"Chance all target mods in one alchemy: {alchProb:P4}",
                $"Expected attempts: ~{Attempts(alchProb)}",
            }
        });

        int unwanted = _assumptions.AlchemyModCount - target.TotalMods;
        for (int i = 0; i < unwanted; i++)
        {
            int remaining = _assumptions.AlchemyModCount - i;
            double hitUnwanted = (double)(remaining - target.TotalMods) / remaining;
            double brickChance = 1.0 - hitUnwanted;
            strategy.Steps.Add(new CraftStep
            {
                Id = $"step-annul-{i + 1}",
                CurrencyName = "Orb of Annulment",
                Description = $"Annul ({remaining - target.TotalMods}/{remaining} safe)",
                SuccessProbability = hitUnwanted,
                BrickProbability = brickChance,
                RestartFromStepId = "step-alch",
                RestartLabel = "Lost a wanted mod → re-alchemy",
                Type = brickChance > 0.3 ? CraftStepType.Brick : CraftStepType.Normal,
                Notes = new() { $"Risk of removing a wanted mod: {brickChance:P1}" }
            });
        }

        strategy.HasBrickRisk = strategy.Steps.Any(s => s.Type == CraftStepType.Brick);
        strategy.Warnings.Add($"Alchemy is modelled as {_assumptions.AlchemyModCount} independent rolls (configurable assumption); prefix/suffix caps are ignored in this estimate.");
        return strategy;
    }

    private CraftingStrategy BuildChaosSpamPath(TargetItemSpec target, Item baseItem)
    {
        double chaosProb = EstimateAlchemyHitProbability(target, baseItem);
        var strategy = new CraftingStrategy
        {
            Id = "chaos-spam",
            Name = "Chaos Orb Spam",
            Description = "Brute-force rerolling. Each Chaos removes one mod and adds one. Extremely low per-attempt success rate.",
        };
        strategy.Steps.Add(new CraftStep
        {
            Id = "step-setup",
            CurrencyName = "Orb of Alchemy",
            Description = "Start: Alchemy on Normal base",
            SuccessProbability = 1.0,
            Type = CraftStepType.Checkpoint,
        });
        strategy.Steps.Add(new CraftStep
        {
            Id = "step-chaos",
            CurrencyName = "Chaos Orb",
            Description = $"Chaos spam until all {target.TotalMods} target mods hit",
            SuccessProbability = chaosProb,
            RestartFromStepId = "step-chaos",
            RestartLabel = "Keep spamming",
            Notes = new()
            {
                "Each chaos: remove 1 random mod, add 1 random mod",
                $"Per-attempt estimate: {chaosProb:P6}",
                $"Expected: ~{Attempts(chaosProb)} chaos orbs",
            }
        });
        strategy.Warnings.Add("Chaos spam probability is a rough estimate: Chaos does not reroll the whole item, so consecutive attempts are not independent.");
        return strategy;
    }

    /// <summary>Add the missing mods one by one (Augment/Regal on magic, Exalt on rare).</summary>
    private CraftingStrategy BuildCompletionPath(Item currentItem, List<TargetMod> missing, string id, string name, string description)
    {
        var strategy = new CraftingStrategy { Id = id, Name = name, Description = description };
        var virtualItem = currentItem.Clone();
        int stepNum = 0;
        foreach (var tm in missing.OrderBy(m => HitProbability(virtualItem, m, virtualItem.Rarity == Rarity.Normal ? Rarity.Magic : Rarity.Rare)))
        {
            string currency = virtualItem.Rarity switch
            {
                Rarity.Magic when virtualItem.AffixCount < 2 && virtualItem.CountOf(tm.AffixType) == 0 && id == "augment-completion" => Augment,
                Rarity.Magic => Regal,
                _ => Exalt,
            };
            AddStep(strategy, virtualItem, tm, currency, ++stepNum, strategy.Steps.Count > 0 ? strategy.Steps[^1].Id : "start", "Annul & retry");
        }
        return strategy;
    }

    private CraftingStrategy? BuildAnnulExaltPath(Item currentItem, TargetItemSpec target, List<TargetMod> missing)
    {
        var wantedMods = currentItem.Affixes.Where(m => target.TargetMods.Any(tm => tm.Matches(m.Def))).ToList();
        var unwantedMods = currentItem.Affixes.Where(m => !wantedMods.Contains(m) && !m.Fractured).ToList();
        if (unwantedMods.Count == 0 || missing.Count == 0) return null;

        var strategy = new CraftingStrategy
        {
            Id = "annul-exalt",
            Name = "Annul + Exalt",
            Description = "Remove unwanted mods with Annulment, then Exalt missing target mods.",
            HasBrickRisk = true,
        };

        var virtualItem = currentItem.Clone();
        // remove the unwanted mods that are in the way: families blocking a target first, then as many as needed for free slots
        var toRemove = unwantedMods.Where(m => missing.Any(tm => tm.Family == m.Def?.Family)).ToList();
        foreach (var type in new[] { AffixType.Prefix, AffixType.Suffix })
        {
            int need = missing.Count(tm => tm.AffixType == type) - (_assumptions.MaxAffixes(Rarity.Rare, type) - currentItem.CountOf(type) + toRemove.Count(m => m.Affix == type));
            toRemove.AddRange(unwantedMods.Where(m => m.Affix == type && !toRemove.Contains(m)).Take(Math.Max(0, need)));
        }
        if (toRemove.Count == 0) return null;

        int step = 0;
        foreach (var victim in toRemove)
        {
            var removable = virtualItem.Affixes.Where(m => !m.Fractured).ToList();
            int wantedOnItem = removable.Count(m => wantedMods.Contains(m));
            double hitUnwanted = removable.Count > 0 ? (double)(removable.Count - wantedOnItem) / removable.Count : 0;
            strategy.Steps.Add(new CraftStep
            {
                Id = $"step-annul-{++step}",
                CurrencyName = "Orb of Annulment",
                Description = $"Annul unwanted ({removable.Count - wantedOnItem}/{removable.Count} safe)",
                SuccessProbability = hitUnwanted,
                BrickProbability = 1.0 - hitUnwanted,
                RestartFromStepId = "start",
                RestartLabel = "Lost a wanted mod → start over",
                Type = CraftStepType.Brick,
                Notes = new() { $"Risk of removing a wanted mod: {1.0 - hitUnwanted:P1}", "Assumes any unwanted mod is fine to remove." }
            });
            virtualItem.Mods.Remove(virtualItem.Mods.First(m => m.ModId == victim.ModId && m.IsAffix));
        }

        int exalt = 0;
        foreach (var tm in missing.OrderBy(m => HitProbability(virtualItem, m, Rarity.Rare)))
            AddStep(strategy, virtualItem, tm, Exalt, ++exalt, strategy.Steps[^1].Id, "Annul & retry", idPrefix: "step-exalt");
        return strategy;
    }

    // ------------------------------------------------------------------ magic-target strategies

    private CraftingStrategy BuildTransmuteSpamPath(TargetItemSpec target, Item baseItem)
    {
        var strategy = new CraftingStrategy
        {
            Id = "transmute-spam",
            Name = "Transmutation Spam",
            Description = "Spam Transmutation on a Normal base until the target mod hits.",
        };
        var mod = target.TargetMods[0];
        if (mod.ResolvedMod != null) AddStep(strategy, baseItem.Clone(), mod, Transmute, 1, "start");
        return strategy;
    }

    private CraftingStrategy BuildTransmuteAugPath(TargetItemSpec target, Item baseItem, int primaryIndex)
    {
        var primary = target.TargetMods[primaryIndex];
        var secondary = target.TargetMods[1 - primaryIndex];
        var strategy = new CraftingStrategy
        {
            Id = $"transmute-aug-{primaryIndex}",
            Name = $"Transmute ({primary.ResolvedMod?.Name ?? primary.Family}) → Augment",
            Description = $"Transmute for {primary.DisplayName}, then Augment for {secondary.DisplayName}.",
        };
        if (primary.ResolvedMod == null || secondary.ResolvedMod == null) return strategy;

        var virtualItem = baseItem.Clone();
        AddStep(strategy, virtualItem, primary, Transmute, 1, "start");
        AddStep(strategy, virtualItem, secondary, Augment, 2, "start");
        return strategy;
    }

    // ------------------------------------------------------------------ steps & probabilities

    /// <summary>Append a "currency → target mod" step, attach Normal/Greater/Perfect variants and simulate the mod on the virtual item.</summary>
    private void AddStep(CraftingStrategy strategy, Item virtualItem, TargetMod tm, string currency, int stepNum, string restartId,
        string? restartLabel = null, string idPrefix = "step")
    {
        var targetRarity = TargetRarityOf(currency);
        double prob = HitProbability(virtualItem, tm, targetRarity);
        var step = new CraftStep
        {
            Id = $"{idPrefix}-{stepNum}",
            CurrencyName = currency,
            Description = $"{currency} → {tm.DisplayName}",
            SuccessProbability = prob,
            RestartFromStepId = restartId,
            RestartLabel = restartLabel ?? (restartId == "start" ? RestartLabel : "Annul & retry"),
            Notes = new() { $"Hit chance: {prob:P2} (1 in {Attempts(prob)})" },
        };
        AttachVariants(step, virtualItem, tm, currency);
        strategy.Steps.Add(step);
        SimulateAddMod(virtualItem, tm, currency);
    }

    private static Rarity TargetRarityOf(string currency) =>
        currency.Contains("Transmutation") || currency.Contains("Augmentation") ? Rarity.Magic : Rarity.Rare;

    /// <summary>Probability that one random addition hits the target (its tier or better).</summary>
    private double HitProbability(Item virtualItem, TargetMod target, Rarity targetRarity, int minModLevel = 0)
    {
        if (target.ResolvedMod == null) return 0;
        return _engine.AdditionDistribution(virtualItem, targetRarity, minModLevel)
            .Where(c => target.Matches(c.Mod)).Sum(c => c.Probability);
    }

    private static string[] GetVariantNames(string baseCurrency) => baseCurrency switch
    {
        Transmute => new[] { Transmute, "Greater Orb of Transmutation", "Perfect Orb of Transmutation" },
        Augment => new[] { Augment, "Greater Orb of Augmentation", "Perfect Orb of Augmentation" },
        Exalt => new[] { Exalt, "Greater Exalted Orb", "Perfect Exalted Orb" },
        Regal => new[] { Regal, "Greater Regal Orb", "Perfect Regal Orb" },
        _ => Array.Empty<string>()
    };

    /// <summary>Compute probabilities for each currency variant (Normal/Greater/Perfect) and mark the best one.</summary>
    private List<CurrencyVariant> ComputeVariants(Item virtualItem, TargetMod target, string baseCurrency)
    {
        var result = GetVariantNames(baseCurrency).Select(name =>
        {
            int minModLevel = _data.FindCurrency(name)?.MinModLevel ?? 0;
            return new CurrencyVariant { Name = name, MinModLevel = minModLevel, Probability = HitProbability(virtualItem, target, TargetRarityOf(baseCurrency), minModLevel) };
        }).ToList();
        var best = result.OrderByDescending(v => v.Probability).ThenBy(v => v.MinModLevel).FirstOrDefault();
        if (best != null) best.IsRecommended = true;
        return result;
    }

    private double EstimateAlchemyHitProbability(TargetItemSpec target, Item baseItem)
    {
        var virtualItem = baseItem.Clone();
        virtualItem.Rarity = Rarity.Normal;
        var dist = _engine.AdditionDistribution(virtualItem, Rarity.Rare);
        int modCount = _assumptions.AlchemyModCount;
        double prob = 1.0;
        foreach (var tm in target.TargetMods)
        {
            if (tm.ResolvedMod == null) return 0;
            double single = dist.Where(c => tm.Matches(c.Mod)).Sum(c => c.Probability);
            if (single == 0) return 0;
            prob *= 1.0 - Math.Pow(1.0 - single, modCount);
        }
        return prob;
    }

    private void AttachVariants(CraftStep step, Item virtualItem, TargetMod target, string baseCurrency)
    {
        var variants = ComputeVariants(virtualItem, target, baseCurrency);
        if (variants.Count == 0) return;
        step.Variants = variants;

        var best = variants.First(v => v.IsRecommended);
        if (best.Name != baseCurrency && best.Probability > step.SuccessProbability)
            step.Notes.Add($"★ Best: {best.Name} ({best.Probability:P2})");

        foreach (var v in variants)
            if (v.Probability == 0 && v.MinModLevel > 0 && target.ResolvedMod != null && target.ResolvedMod.Level < v.MinModLevel)
                step.Notes.Add($"{v.Name}: 0% — no matching tier at or above its minimum modifier level {v.MinModLevel} can roll here");
    }

    /// <summary>
    /// Promote each step to its best currency variant (e.g. Greater Transmutation instead of normal Transmutation)
    /// and recompute the overall probability so sorting reflects the actual best approach.
    /// </summary>
    private static void ApplyBestVariants(CraftingStrategy strategy)
    {
        double overall = 1.0;
        foreach (var step in strategy.Steps)
        {
            var best = step.Variants.FirstOrDefault(v => v.IsRecommended);
            if (best != null && best.Probability > step.SuccessProbability)
            {
                step.SuccessProbability = best.Probability;
                step.CurrencyName = best.Name;
                for (int i = 0; i < step.Notes.Count; i++)
                    if (step.Notes[i].StartsWith("Hit chance"))
                        step.Notes[i] = $"Hit chance: {best.Probability:P2} (1 in {Attempts(best.Probability)}) — {best.Name}";
                step.Notes.RemoveAll(n => n.StartsWith("★ Best:"));
            }
            overall *= step.SuccessProbability;
        }
        strategy.OverallProbability = strategy.Steps.Count > 0 ? overall : 0;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Order for "one mod at a time" crafts: the rarest mod first, then the rarest mod of the other affix type (both fit on a magic item),
    /// then the remaining mods rarest first.
    /// </summary>
    private List<TargetMod> MagicFirstOrder(List<TargetMod> mods, Item baseItem)
    {
        var byRarity = mods.OrderBy(m => HitProbability(baseItem, m, Rarity.Rare)).ToList();
        if (byRarity.Count <= 1) return byRarity;
        var first = byRarity[0];
        var second = byRarity.Skip(1).FirstOrDefault(m => m.AffixType != first.AffixType);
        var result = new List<TargetMod> { first };
        if (second != null) result.Add(second);
        result.AddRange(byRarity.Skip(1).Where(m => m != second));
        return result;
    }

    private static void SimulateAddMod(Item virtualItem, TargetMod target, string currency)
    {
        if (target.ResolvedMod == null) return;
        if (currency.Contains("Transmutation")) virtualItem.Rarity = Rarity.Magic;
        else if (currency.Contains("Regal")) virtualItem.Rarity = Rarity.Rare;
        virtualItem.Mods.Add(new ItemMod { ModId = target.ResolvedMod.Id, Def = target.ResolvedMod, Affix = target.AffixType, Kind = ModKind.Explicit });
    }

    private static string Attempts(double p) => p > 0 ? Math.Ceiling(1.0 / p).ToString("N0") : "∞";

    // ------------------------------------------------------------------ Mermaid generation

    /// <summary>Generate a Mermaid flowchart from a strategy.</summary>
    public static string ToMermaid(CraftingStrategy strategy)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("flowchart TD");
        sb.AppendLine($"    start([\"{EscapeMermaid(strategy.StartLabel)}\"])");

        string prevId = "start";
        foreach (var step in strategy.Steps)
        {
            string safeDesc = EscapeMermaid(step.Description);
            string probText = step.SuccessProbability < 1.0 ? $"<br/>{step.SuccessProbability:P1}" : "";

            sb.AppendLine(step.Type switch
            {
                CraftStepType.Brick => $"    {step.Id}{{\"{safeDesc}{probText}\"}}",
                CraftStepType.Checkpoint => $"    {step.Id}([\"{safeDesc}\"])",
                CraftStepType.Decision => $"    {step.Id}{{{{\"{safeDesc}{probText}\"}}}}",
                _ => $"    {step.Id}[\"{safeDesc}{probText}\"]",
            });
            sb.AppendLine($"    {prevId} -->|\"{EscapeMermaid(step.CurrencyName)}\"| {step.Id}");

            if (step.RestartFromStepId != null && step.SuccessProbability < 1.0)
            {
                double failProb = 1.0 - step.SuccessProbability;
                sb.AppendLine($"    {step.Id} -.->|\"{failProb:P0} {EscapeMermaid(step.RestartLabel ?? "Retry")}\"| {step.RestartFromStepId}");
            }
            prevId = step.Id;
        }

        sb.AppendLine($"    {prevId} --> finish([\"Target Item\"])");
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

    private static string EscapeMermaid(string text) => text.Replace("\"", "'").Replace("\n", "<br/>");
}
