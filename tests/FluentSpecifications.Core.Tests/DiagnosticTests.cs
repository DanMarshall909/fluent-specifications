using System.Linq.Expressions;
using System.Collections;
using FluentSpecifications;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void Matches_wraps_a_leaf_exception_with_rule_identity_and_node_path()
    {
        var failure = new InvalidOperationException("boom");
        var broken = Rule("broken", "Broken", _ => ExceptionProbe.Throw(failure));

        var exception = Assert.Throws<SpecificationEvaluationException>(() =>
            broken.Matches(new Order()));

        Assert.Equal("order.broken", exception.RuleId);
        Assert.Equal("$", exception.NodePath);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public void Matches_reports_the_path_of_a_nested_broken_leaf()
    {
        var failure = new InvalidOperationException("boom");
        var passing = Rule("passing", "Passing", _ => true);
        var broken = Rule("broken", "Broken", _ => ExceptionProbe.Throw(failure));

        var exception = Assert.Throws<SpecificationEvaluationException>(() =>
            passing.And(broken).Matches(new Order()));

        Assert.Equal("order.broken", exception.RuleId);
        Assert.Equal("$.right", exception.NodePath);
    }

    [Fact]
    public void Check_returns_a_structured_business_failure()
    {
        var paid = Spec.Define<Order>(
            id: "order.paid",
            name: "Paid",
            predicate: order => order.Paid,
            failure: "Payment has not been received.",
            code: "payment-required",
            path: "PaymentStatus");

        var result = paid.Check(new Order());

        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.False(result.Passed);
        Assert.True(result.IsComplete);
        Assert.Empty(result.Errors);

        var ruleFailure = Assert.Single(result.Failures);
        Assert.Equal(RuleFailureKind.Rule, ruleFailure.Kind);
        Assert.Equal("order.paid", ruleFailure.RuleId);
        Assert.Equal("Paid", ruleFailure.Name);
        Assert.Equal("Payment has not been received.", ruleFailure.Message);
        Assert.Equal("payment-required", ruleFailure.Code);
        Assert.Equal("PaymentStatus", ruleFailure.Path);
        Assert.Equal("$", ruleFailure.NodePath);
        Assert.Empty(ruleFailure.Causes);
    }

    [Fact]
    public void Check_returns_an_error_instead_of_a_business_failure_for_an_exception()
    {
        var failure = new InvalidOperationException("boom");
        var broken = Rule("broken", "Broken", _ => ExceptionProbe.Throw(failure));

        var result = broken.Check(new Order());

        Assert.Equal(CheckOutcome.Error, result.Outcome);
        Assert.Empty(result.Failures);
        var error = Assert.Single(result.Errors);
        Assert.Equal("order.broken", error.RuleId);
        Assert.Equal("$", error.NodePath);
        Assert.Same(failure, error.Exception);
    }

    [Fact]
    public void Complete_and_is_failed_by_a_false_branch_and_still_reports_other_errors()
    {
        var failure = new InvalidOperationException("boom");
        var unpaid = Rule("paid", "Paid", _ => false, "Payment is required.");
        var broken = Rule("broken", "Broken", _ => ExceptionProbe.Throw(failure));

        var result = unpaid.And(broken).Check(new Order());

        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.True(result.IsComplete);
        Assert.Equal("order.paid", Assert.Single(result.Failures).RuleId);
        Assert.Equal("order.broken", Assert.Single(result.Errors).RuleId);
    }

    [Fact]
    public void Complete_or_is_passed_by_a_true_branch_and_still_reports_other_errors()
    {
        var failure = new InvalidOperationException("boom");
        var passing = Rule("passing", "Passing", _ => true);
        var broken = Rule("broken", "Broken", _ => ExceptionProbe.Throw(failure));

        var result = passing.Or(broken).Check(new Order());

        Assert.Equal(CheckOutcome.Passed, result.Outcome);
        Assert.True(result.Passed);
        Assert.Empty(result.Failures);
        Assert.Equal("order.broken", Assert.Single(result.Errors).RuleId);
    }

    [Fact]
    public void Failed_or_keeps_its_alternatives_grouped()
    {
        var paid = Rule("paid", "Paid", _ => false, "Payment is required.");
        var manual = Rule("manual", "Manual override", _ => false, "Approval is required.");

        var result = paid.Or(manual).Check(new Order());

        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        var alternatives = Assert.Single(result.Failures);
        Assert.Equal(RuleFailureKind.Alternatives, alternatives.Kind);
        Assert.Equal(2, alternatives.Causes.Count);
        Assert.Equal("order.paid", alternatives.Causes[0].RuleId);
        Assert.Equal("order.manual", alternatives.Causes[1].RuleId);
    }

    [Fact]
    public void Named_wraps_child_failures_in_its_domain_message()
    {
        var paid = Rule("paid", "Paid", order => order.Paid, "Payment is required.");
        var addressed = Rule(
            "addressed",
            "Has delivery address",
            order => order.HasAddress,
            "Address is required.");

        var canShip = paid.And(addressed).Named(
            "order.can-ship",
            "Can ship",
            "The order is not ready to ship.");

        var result = canShip.Check(new Order(Paid: true));

        var named = Assert.Single(result.Failures);
        Assert.Equal(RuleFailureKind.Rule, named.Kind);
        Assert.Equal("order.can-ship", named.RuleId);
        Assert.Equal("The order is not ready to ship.", named.Message);
        Assert.Equal("order.addressed", Assert.Single(named.Causes).RuleId);
    }

    [Fact]
    public void Failed_negation_uses_a_neutral_message()
    {
        var suspended = Rule("suspended", "Suspended", _ => true);

        var result = suspended.Not.Check(new Order());

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RuleFailureKind.Negation, failure.Kind);
        Assert.Equal("Expected the rule not to match.", failure.Message);
        Assert.DoesNotContain("not suspended", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Short_circuit_diagnostics_mark_the_result_as_incomplete()
    {
        var probe = new EvaluationProbe();
        var failing = Rule("failing", "Failing", _ => false);
        var right = Rule("right", "Right", _ => probe.Return(true));

        var result = failing.And(right).Check(new Order(), CheckOptions.ShortCircuit);

        Assert.Equal(CheckOutcome.Failed, result.Outcome);
        Assert.False(result.IsComplete);
        Assert.False(probe.WasEvaluated);
    }

    [Fact]
    public void Diagnostic_collections_cannot_be_mutated_through_runtime_casts()
    {
        var paid = Rule("paid", "Paid", _ => false);
        var manual = Rule("manual", "Manual override", _ => false);
        var result = paid.Or(manual).Check(new Order());

        var failures = Assert.IsAssignableFrom<IList>(result.Failures);
        Assert.Throws<NotSupportedException>(() => failures[0] = failures[0]);

        var alternatives = Assert.Single(result.Failures);
        var causes = Assert.IsAssignableFrom<IList>(alternatives.Causes);
        Assert.Throws<NotSupportedException>(() => causes[0] = causes[0]);
    }

    [Fact]
    public void Explicit_diagnostic_context_is_snapshotted_and_reported()
    {
        var context = new Dictionary<string, object?>
        {
            ["minimum"] = 100m
        };
        var valuable = Spec.Define<Order>(
            id: "order.valuable",
            name: "Valuable",
            predicate: _ => false,
            failure: "The order total is too low.",
            context: context);

        context["minimum"] = 200m;

        var failure = Assert.Single(valuable.Check(new Order()).Failures);
        Assert.Equal(100m, failure.Context["minimum"]);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary)failure.Context)["minimum"] = 300m);
    }

    private static Spec<Order> Rule(
        string id,
        string name,
        Expression<Func<Order, bool>> predicate,
        string? failure = null) =>
        Spec.Define($"order.{id}", name, predicate, failure);

    private static class ExceptionProbe
    {
        public static bool Throw(Exception exception) => throw exception;
    }

    private sealed class EvaluationProbe
    {
        public bool WasEvaluated { get; private set; }

        public bool Return(bool result)
        {
            WasEvaluated = true;
            return result;
        }
    }

    private sealed record Order(bool Paid = false, bool HasAddress = false);
}
