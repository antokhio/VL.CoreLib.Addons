using System.Reflection;
using VL.Core.PublicAPI;
using VL.CoreLib.Addons.Primitives;

namespace VL.CoreLib.Addons.Tests;

[TestFixture]
public class IsAssignedRegionTests
{
    private static readonly InputDescription Link = new(
        "link",
        typeof(int),
        typeof(int),
        IsLink: true
    );

    // VL sets AccumulatorId to the id of the input control point on both sides of the pair.
    private static readonly InputDescription AccumulatorInput = new(
        "acc",
        typeof(float),
        typeof(float),
        AccumulatorId: "acc"
    );
    private static readonly OutputDescription AccumulatorOutput = new(
        "accOut",
        typeof(float),
        typeof(float),
        AccumulatorId: "acc"
    );

    private readonly List<RecordingInlay> _createdInlays = new();
    private IsAssignedRegion<string> _region = null!;

    [SetUp]
    public void SetUp()
    {
        _createdInlays.Clear();
        _region = new IsAssignedRegion<string>();
        AsRegion(_region).SetPatchInlayFactory(Compiled(inlay => { }));
    }

    [TearDown]
    public void TearDown() => _region.Dispose();

    [Test]
    public void Update_Null_DoesNotExecutePatch()
    {
        _region.Update(null, out var isAssigned);

        Assert.Multiple(() =>
        {
            Assert.That(isAssigned, Is.False);
            Assert.That(_createdInlays, Is.Empty);
        });
    }

    [Test]
    public void Update_Assigned_PassesValueIntoPatch()
    {
        _region.Update("a", out var isAssigned);
        _region.Update("b", out _);

        Assert.Multiple(() =>
        {
            Assert.That(isAssigned, Is.True);
            Assert.That(_createdInlays, Has.Count.EqualTo(1));
            Assert.That(_createdInlays[0].Values, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public void Update_AssignedAfterNull_KeepsPatchState()
    {
        _region.Update("a", out _);
        _region.Update(null, out _);
        _region.Update("b", out _);

        Assert.Multiple(() =>
        {
            Assert.That(_createdInlays, Has.Count.EqualTo(1));
            Assert.That(_createdInlays[0].Values, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public void TypeParameter_IsConstrainedToReferenceTypes()
    {
        var attributes = typeof(IsAssignedRegion<>).GetGenericArguments()[0].GenericParameterAttributes;

        Assert.That(
            attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint),
            Is.True
        );
    }

    [Test]
    public void RegionAttribute_SupportsAccumulatorsOnly()
    {
        var attribute = typeof(IsAssignedRegion<>).GetCustomAttribute<RegionAttribute>();

        Assert.That(
            attribute?.SupportedBorderControlPoints,
            Is.EqualTo(ControlPointType.Accumulator)
        );
    }

    [Test]
    public void RetrieveInput_Link_PassesOuterValueInside()
    {
        object? linkValue = null;
        AsRegion(_region)
            .SetPatchInlayFactory(
                Compiled(inlay => AsRegion(_region).RetrieveInput(Link, inlay, out linkValue))
            );

        AsRegion(_region).AcknowledgeInput(Link, 7);
        _region.Update("a", out _);

        Assert.That(linkValue, Is.EqualTo(7));
    }

    [Test]
    public void RetrieveInput_Accumulator_PassesOuterValueInside()
    {
        object? innerValue = null;
        AsRegion(_region)
            .SetPatchInlayFactory(
                Compiled(inlay =>
                    AsRegion(_region).RetrieveInput(AccumulatorInput, inlay, out innerValue)
                )
            );

        AsRegion(_region).AcknowledgeInput(AccumulatorInput, 1f);
        _region.Update("a", out _);

        Assert.That(innerValue, Is.EqualTo(1f));
    }

    [Test]
    public void RetrieveOutput_AccumulatorPatchExecuted_ReturnsInnerValue()
    {
        AsRegion(_region)
            .SetPatchInlayFactory(
                Compiled(inlay => AsRegion(_region).AcknowledgeOutput(AccumulatorOutput, inlay, 2f))
            );

        AsRegion(_region).AcknowledgeInput(AccumulatorInput, 1f);
        _region.Update("a", out _);
        AsRegion(_region).RetrieveOutput(AccumulatorOutput, out var outerValue);

        Assert.That(outerValue, Is.EqualTo(2f));
    }

    [Test]
    public void RetrieveOutput_AccumulatorPatchNotExecutedInThisFrame_ReturnsInputValue()
    {
        AsRegion(_region)
            .SetPatchInlayFactory(
                Compiled(inlay => AsRegion(_region).AcknowledgeOutput(AccumulatorOutput, inlay, 2f))
            );
        AsRegion(_region).AcknowledgeInput(AccumulatorInput, 1f);
        _region.Update("a", out _);

        AsRegion(_region).AcknowledgeInput(AccumulatorInput, 3f);
        _region.Update(null, out _);
        AsRegion(_region).RetrieveOutput(AccumulatorOutput, out var outerValue);

        Assert.That(outerValue, Is.EqualTo(3f));
    }

    [Test]
    public void RetrieveOutput_AccumulatorWithoutAcknowledgedInput_ReturnsNull()
    {
        _region.Update(null, out _);
        AsRegion(_region).RetrieveOutput(AccumulatorOutput, out var outerValue);

        Assert.That(outerValue, Is.Null);
    }

    [Test]
    public void SetPatchInlayFactory_NewlyCompiledFactory_KeepsExistingInlay()
    {
        _region.Update("a", out _);

        AsRegion(_region).SetPatchInlayFactory(Compiled(inlay => { }));
        _region.Update("b", out _);

        Assert.Multiple(() =>
        {
            Assert.That(_createdInlays, Has.Count.EqualTo(1));
            Assert.That(_createdInlays[0].Values, Is.EqualTo(new[] { "a", "b" }));
        });
    }

    [Test]
    public void Dispose_AfterPatchExecuted_DisposesInlay()
    {
        _region.Update("a", out _);

        _region.Dispose();

        Assert.That(_createdInlays[0].IsDisposed, Is.True);
    }

    [Test]
    public void Update_AssignedWithoutPatchInlayFactory_ThrowsInvalidOperationException()
    {
        using var region = new IsAssignedRegion<string>();

        Assert.Throws<InvalidOperationException>(() => region.Update("a", out _));
    }

    [Test]
    public void Update_AfterDispose_ThrowsObjectDisposedException()
    {
        _region.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _region.Update("a", out _));
    }

    private static IRegion<IIsAssignedInlay<string>> AsRegion(IsAssignedRegion<string> region) =>
        region;

    private Func<IIsAssignedInlay<string>> Compiled(Action<RecordingInlay> body) =>
        () =>
        {
            var inlay = new RecordingInlay(body);
            _createdInlays.Add(inlay);
            return inlay;
        };

    private sealed class RecordingInlay(Action<RecordingInlay> body)
        : IIsAssignedInlay<string>,
            IDisposable
    {
        public List<string> Values { get; } = new();

        public bool IsDisposed { get; private set; }

        public void Update(string value)
        {
            Values.Add(value);
            body(this);
        }

        public void Dispose() => IsDisposed = true;
    }
}
