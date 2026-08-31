---
title: Diagnostics
description: Distinguish normal business-rule failures from evaluation errors and produce complete, structured explanations without leaking candidate data.
order: 4
section: Evaluation
---

There are two deliberate evaluation modes. `Matches` answers a fast Boolean
question. `Check` produces an explanation. Neither converts an exception into a
normal failed rule.

## Fast Boolean evaluation

Use `Matches` when the caller needs only yes or no. Generated domain properties
use the same path. `And` and `Or` short-circuit from left to right.

If a predicate throws, `Matches` raises
`SpecificationEvaluationException`. The exception retains the rule ID, node
path, and original exception without serializing the candidate into its
message:

```csharp symbol="M:FluentSpecifications.Core.Tests.DiagnosticTests.Matches_wraps_a_leaf_exception_with_rule_identity_and_node_path"
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
```

## Structured explanations

Use `Check` when an application must explain why a rule did not pass:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.ExplainWhyShippingIsBlocked(FluentSpecifications.Examples.OrderFulfilment.Order)"
public static CheckResult ExplainWhyShippingIsBlocked(Order order) =>
    CanShip.Check(order);
```

A failure can carry a stable ID, safe message, machine code, domain path, node
path, and explicitly supplied context:

```csharp symbol="M:FluentSpecifications.Core.Tests.DiagnosticTests.Check_returns_a_structured_business_failure"
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
```

`CheckResult.Outcome` is `Passed`, `Failed`, or `Error`. A returned `false` is a
business failure; a thrown predicate is an evaluation error. Callers can handle
those outcomes differently without inspecting exception strings.

## Complete is the default

Complete diagnostics evaluate every leaf from left to right, even after the
Boolean outcome is known. That permits a failed `And` to report all failed
requirements and permits a passing `Or` to retain an error found in another
alternative.

`CheckOptions.ShortCircuit` is available for expensive trees. The result then
sets `IsComplete` to `false`, making the tradeoff visible rather than implying
that an abbreviated explanation is exhaustive.

## Alternatives remain alternatives

When an `Or` fails, its branches are grouped rather than flattened into a list
that would incorrectly imply every alternative was mandatory:

```csharp symbol="M:FluentSpecifications.Core.Tests.DiagnosticTests.Failed_or_keeps_its_alternatives_grouped"
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
```

Named compositions wrap their child failures in the domain message while
retaining the underlying causes. This allows a UI to start with “The order is
not ready to ship” and reveal the specific payment or address reasons when
needed.

## Safe by default

Candidate values, captured arguments, expression bodies, and exception details
are omitted from default rendering. Add diagnostic context explicitly, and only
when it is safe to retain and display. The library snapshots that context so a
later mutation cannot rewrite a recorded explanation.
