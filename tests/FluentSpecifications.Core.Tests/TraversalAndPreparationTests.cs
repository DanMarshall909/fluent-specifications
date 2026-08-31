using System.Linq.Expressions;
using FluentSpecifications;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class TraversalAndPreparationTests
{
    [Fact]
    public void Visitor_observes_the_named_boolean_tree_in_child_order()
    {
        var paid = Rule("paid", "Paid", order => order.Paid);
        var priority = Rule("priority", "High priority", order => order.Priority);
        var manual = Rule("manual", "Manual override", order => order.ManualOverride);
        var canShip = paid
            .And(priority.Or(manual))
            .Named("order.can-ship", "Can ship");

        var structure = canShip.Accept(new StructureVisitor());

        Assert.Equal(
            "named:order.can-ship(and(leaf:order.paid,or(leaf:order.priority,leaf:order.manual)))",
            structure);
    }

    [Fact]
    public void Visitor_receives_the_original_typed_leaf_expression()
    {
        var paid = Rule("paid", "Paid", order => order.Paid);
        var visitor = new PredicateVisitor(new Order(Paid: true));

        var result = paid.Accept(visitor);

        Assert.True(result);
        Assert.Equal("order.paid", visitor.RuleId);
    }

    [Fact]
    public void Accept_rejects_a_null_visitor()
    {
        var paid = Rule("paid", "Paid", order => order.Paid);

        Assert.Throws<ArgumentNullException>(() => paid.Accept<string>(null!));
    }

    [Fact]
    public void Successful_preparation_exposes_only_its_plan()
    {
        var preparation = Preparation<string>.Succeeded("prepared-plan");

        Assert.True(preparation.IsSuccess);
        Assert.Empty(preparation.Errors);
        Assert.Equal("prepared-plan", preparation.GetPlanOrThrow());
    }

    [Fact]
    public void Failed_preparation_requires_errors_and_throws_a_structured_exception()
    {
        var issue = new TranslationError(
            code: "unsupported-call",
            message: "The provider cannot translate this method.",
            nodePath: "$.right",
            ruleId: "order.priority");
        var preparation = Preparation<string>.Failed([issue]);

        Assert.False(preparation.IsSuccess);
        Assert.Equal(issue, Assert.Single(preparation.Errors));

        var exception = Assert.Throws<SpecificationTranslationException>(
            preparation.GetPlanOrThrow);
        Assert.Equal(issue, Assert.Single(exception.Errors));
    }

    [Fact]
    public void Failed_preparation_rejects_an_empty_error_collection()
    {
        Assert.Throws<ArgumentException>(() =>
            Preparation<string>.Failed(Array.Empty<TranslationError>()));
    }

    private static Spec<Order> Rule(
        string id,
        string name,
        Expression<Func<Order, bool>> predicate) =>
        Spec.Define($"order.{id}", name, predicate);

    private sealed class StructureVisitor : ISpecVisitor<Order, string>
    {
        public string VisitAlways() => "always";

        public string VisitNever() => "never";

        public string VisitLeaf(
            RuleDescriptor rule,
            Expression<Func<Order, bool>> predicate) => $"leaf:{rule.Id}";

        public string VisitNamed(RuleDescriptor rule, Spec<Order> child) =>
            $"named:{rule.Id}({child.Accept(this)})";

        public string VisitAnd(Spec<Order> left, Spec<Order> right) =>
            $"and({left.Accept(this)},{right.Accept(this)})";

        public string VisitOr(Spec<Order> left, Spec<Order> right) =>
            $"or({left.Accept(this)},{right.Accept(this)})";

        public string VisitNot(Spec<Order> child) => $"not({child.Accept(this)})";
    }

    private sealed class PredicateVisitor(Order candidate) : ISpecVisitor<Order, bool>
    {
        public string? RuleId { get; private set; }

        public bool VisitAlways() => true;

        public bool VisitNever() => false;

        public bool VisitLeaf(
            RuleDescriptor rule,
            Expression<Func<Order, bool>> predicate)
        {
            RuleId = rule.Id;
            return predicate.Compile()(candidate);
        }

        public bool VisitNamed(RuleDescriptor rule, Spec<Order> child) => child.Accept(this);

        public bool VisitAnd(Spec<Order> left, Spec<Order> right) =>
            left.Accept(this) && right.Accept(this);

        public bool VisitOr(Spec<Order> left, Spec<Order> right) =>
            left.Accept(this) || right.Accept(this);

        public bool VisitNot(Spec<Order> child) => !child.Accept(this);
    }

    private sealed record Order(
        bool Paid = false,
        bool Priority = false,
        bool ManualOverride = false);
}
