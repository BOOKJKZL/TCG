using System;
using System.Collections.Generic;
using System.Linq;
using Gacha.Domain;
using NUnit.Framework;

public class GachaEngineTests
{
    [Test]
    public void Draw_UsesMultipleSlotsAndProducesRevealOrder()
    {
        Fixture fixture = new Fixture();
        WeightedPool commonPool = new WeightedPool("common-pool", new[]
        {
            new WeightedPoolEntry(fixture.CommonA.Id, 1d)
        });
        WeightedPool rarePool = new WeightedPool("rare-pool", new[]
        {
            new WeightedPoolEntry(fixture.Rare.Id, 1d)
        });
        ProductDrawRules rules = new ProductDrawRules(fixture.Product.Id,
            new[] { commonPool, rarePool },
            new[]
            {
                new SlotRule("common-slot", commonPool.Id, 2, 0),
                new SlotRule("rare-slot", rarePool.Id, 1, 10)
            });

        ProductDrawResult result = new GachaEngine().Draw(fixture.Catalog, rules, 0, new MinimumRandom());

        Assert.That(result.Printings.Select(item => item.PrintingId),
            Is.EqualTo(new[] { fixture.CommonA.Id, fixture.CommonA.Id, fixture.Rare.Id }));
        Assert.That(result.Printings.Select(item => item.RevealOrder), Is.EqualTo(new[] { 0, 1, 10 }));
    }

    [Test]
    public void Draw_SameSeedProducesSameResult()
    {
        Fixture fixture = new Fixture();
        ProductDrawRules rules = SimulatedProductRuleFactory.CreateUniform(fixture.Catalog, fixture.Product.Id, 8);
        GachaEngine engine = new GachaEngine();

        string[] first = engine.Draw(fixture.Catalog, rules, 0, new SystemGachaRandomSource(712))
            .Printings.Select(item => item.PrintingId).ToArray();
        string[] second = engine.Draw(fixture.Catalog, rules, 0, new SystemGachaRandomSource(712))
            .Printings.Select(item => item.PrintingId).ToArray();

        Assert.That(first, Is.EqualTo(second));
    }

    [Test]
    public void Draw_GuaranteeReplacesOnlyRequiredNonQualifyingSlot()
    {
        Fixture fixture = new Fixture();
        WeightedPool commonPool = new WeightedPool("common-pool", new[]
        {
            new WeightedPoolEntry(fixture.CommonA.Id, 1d)
        });
        WeightedPool guaranteePool = new WeightedPool("guarantee-pool", new[]
        {
            new WeightedPoolEntry(fixture.Rare.Id, 1d)
        });
        SlotRule slot = new SlotRule("main", commonPool.Id, 5);
        GuaranteeRule guarantee = new GuaranteeRule(
            "ten-pack-rare",
            10,
            1,
            guaranteePool.Id,
            new[] { fixture.Rare.RarityId },
            new[] { slot.Id });
        ProductDrawRules rules = new ProductDrawRules(
            fixture.Product.Id,
            new[] { commonPool, guaranteePool },
            new[] { slot },
            new[] { guarantee });

        ProductDrawResult result = new GachaEngine().Draw(fixture.Catalog, rules, 9, new MinimumRandom());

        Assert.That(result.GuaranteeApplied, Is.True);
        Assert.That(result.Printings.Count(item => item.PrintingId == fixture.Rare.Id), Is.EqualTo(1));
        Assert.That(result.Printings.Count(item => item.IsGuaranteeReplacement), Is.EqualTo(1));
    }

    [Test]
    public void Draw_ThrowsClearErrorWhenUniquePoolCannotFillSlot()
    {
        Fixture fixture = new Fixture();
        WeightedPool pool = new WeightedPool("two-cards", new[]
        {
            new WeightedPoolEntry(fixture.CommonA.Id, 1d),
            new WeightedPoolEntry(fixture.CommonB.Id, 1d)
        });
        ProductDrawRules rules = new ProductDrawRules(
            fixture.Product.Id,
            new[] { pool },
            new[] { new SlotRule("unique", pool.Id, 3, 0, false) });

        GachaRuleException exception = Assert.Throws<GachaRuleException>(() =>
            new GachaEngine().Draw(fixture.Catalog, rules, 0, new MinimumRandom()));

        Assert.That(exception.Message, Does.Contain("no available entries"));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            LanguageDefinition language = new LanguageDefinition("en", Name("English"));
            GameDefinition game = new GameDefinition("game", Name("Game"), new[] { language.Id });
            SetDefinition set = new SetDefinition("set", game.Id, Name("Set"));
            RarityDefinition common = new RarityDefinition("common", game.Id, Name("Common"), 0);
            RarityDefinition rare = new RarityDefinition("rare", game.Id, Name("Rare"), 1);
            VariantDefinition normal = new VariantDefinition("normal", game.Id, Name("Normal"));
            CollectibleItemDefinition itemA = new CollectibleItemDefinition("item-a", game.Id, Name("A"), "card");
            CollectibleItemDefinition itemB = new CollectibleItemDefinition("item-b", game.Id, Name("B"), "card");
            CollectibleItemDefinition itemR = new CollectibleItemDefinition("item-r", game.Id, Name("R"), "card");
            CommonA = Printing("printing-a", itemA, common);
            CommonB = Printing("printing-b", itemB, common);
            Rare = Printing("printing-r", itemR, rare);
            Product = new ProductDefinition("product", game.Id, set.Id, Name("Pack"), "booster",
                new[] { CommonA.Id, CommonB.Id, Rare.Id });
            Catalog = new UniversalCatalog(
                new[] { language }, new[] { game }, new[] { set },
                new[] { itemA, itemB, itemR }, new[] { common, rare }, new[] { normal },
                new[] { CommonA, CommonB, Rare }, new[] { Product });

            PrintingDefinition Printing(string id, CollectibleItemDefinition item, RarityDefinition rarity)
            {
                return new PrintingDefinition(id, item.Id,
                    new PrintingIdentity(game.Id, set.Id, id, language.Id, normal.Id),
                    rarity.Id, item.Names);
            }
        }

        public UniversalCatalog Catalog { get; }
        public ProductDefinition Product { get; }
        public PrintingDefinition CommonA { get; }
        public PrintingDefinition CommonB { get; }
        public PrintingDefinition Rare { get; }
    }

    private sealed class MinimumRandom : IGachaRandomSource
    {
        public double Value => 0d;
        public int Range(int minInclusive, int maxExclusive) => minInclusive;
    }

    private static Dictionary<string, string> Name(string value)
    {
        return new Dictionary<string, string> { ["en"] = value };
    }
}
