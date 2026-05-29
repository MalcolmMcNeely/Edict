using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.Json;

using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Telemetry;

namespace Edict.Core.Tests.DeadLetter;

public sealed class DeadLetterFailureClassifierTests
{
    [Theory]
    [InlineData(typeof(TimeoutException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Timeout))]
    [InlineData(typeof(OperationCanceledException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Timeout))]
    [InlineData(typeof(JsonException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization))]
    [InlineData(typeof(SerializationException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization))]
    [InlineData(typeof(HttpRequestException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate))]
    [InlineData(typeof(IOException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate))]
    [InlineData(typeof(InvalidOperationException), nameof(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled))]
    public void Classify_ShouldMapKnownExceptionType_ToAllowlistBucket(Type exceptionType, string expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(expected, bucket);
    }

    [Fact]
    public void Classify_ShouldMapSocketException_ToSubstrate()
    {
        var exception = new SocketException();

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictEnvelopeOverflowException_ToSerialization()
    {
        var exception = new EdictEnvelopeOverflowException(Guid.NewGuid(), "FooEvent", 99_000);

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization, bucket);
    }

    [Fact]
    public void Classify_ShouldMapAnyExceptionWhoseTypeNameContainsSaturated_ToSaturated()
    {
        // Forward-compat with EdictOutboxSaturatedException (memory: dead-letter-grain-backed-design).
        var exception = new SyntheticSaturatedException();

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Saturated, bucket);
    }

    [Fact]
    public void Classify_ShouldMapUnknownExceptionType_ToUnhandled()
    {
        var exception = new ApplicationException("nope");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Unhandled, bucket);
    }

    [Theory]
    [InlineData(EdictUnregisteredTypeException.Kind.Event)]
    [InlineData(EdictUnregisteredTypeException.Kind.RowAlias)]
    [InlineData(EdictUnregisteredTypeException.Kind.Stream)]
    public void Classify_ShouldMapEdictUnregisteredTypeException_ToWiring(
        EdictUnregisteredTypeException.Kind kind)
    {
        var exception = new EdictUnregisteredTypeException(kind, "Some.Type", "scan your assembly");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Wiring, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictClaimCheckFetchException_KeyMalformed_ToSerialization()
    {
        var exception = new EdictClaimCheckFetchException(
            EdictClaimCheckFetchException.Reason.KeyMalformed, "bad-key", "not a guid");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Serialization, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictClaimCheckFetchException_PayloadMissing_ToSubstrate()
    {
        var exception = new EdictClaimCheckFetchException(
            EdictClaimCheckFetchException.Reason.PayloadMissing, "abc123", "no row");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.Substrate, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictSagaCoordinationException_ToConsumerBug()
    {
        var exception = new EdictSagaCoordinationException("WorkflowSaga dispatched twice");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug, bucket);
    }

    [Fact]
    public void Classify_ShouldMapEdictInternalInvariantException_ToInternalBug()
    {
        var exception = new EdictInternalInvariantException("framework-internal invariant violated");

        var bucket = DeadLetterFailureClassifier.Classify(exception);

        Assert.Equal(SemanticConventions.DeadLetter.Tags.FailureReasonValues.InternalBug, bucket);
    }

    [Fact]
    public void EdictUnsupportedEffectKindException_AndEdictMissingRouteKeyException_AreInstantiable_AsPromoterMarkers()
    {
        var unsupported = new EdictUnsupportedEffectKindException("kind: Unknown");
        var missing = new EdictMissingRouteKeyException("OrderPlacedCommand has no [EdictRouteKey]");

        Assert.Equal("kind: Unknown", unsupported.Message);
        Assert.Equal("OrderPlacedCommand has no [EdictRouteKey]", missing.Message);
    }

    [Fact]
    public void FailureReasonValues_ShouldExposeThreeNewBuckets_ForRuntimeTypedThrows()
    {
        Assert.Equal("Wiring", SemanticConventions.DeadLetter.Tags.FailureReasonValues.Wiring);
        Assert.Equal("ConsumerBug", SemanticConventions.DeadLetter.Tags.FailureReasonValues.ConsumerBug);
        Assert.Equal("InternalBug", SemanticConventions.DeadLetter.Tags.FailureReasonValues.InternalBug);
    }

    sealed class SyntheticSaturatedException : Exception
    {
        public SyntheticSaturatedException() : base("simulated saturation") { }
    }
}
