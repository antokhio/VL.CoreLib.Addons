using System.Reactive.Disposables;
using System.Reflection;
using VL.Core.PublicAPI;
using VL.CoreLib.Addons.Primitives;

namespace VL.CoreLib.Addons.Tests;

[TestFixture]
public class IsAssignedRegion2Tests
{
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

    [Test]
    public void TypeParameter_IsNotConstrained()
    {
        var attributes = typeof(IsAssignedRegion2<>).GetGenericArguments()[0].GenericParameterAttributes;

        Assert.That(
            attributes & GenericParameterAttributes.SpecialConstraintMask,
            Is.EqualTo(GenericParameterAttributes.None)
        );
    }

    [Test]
    public void RegionAttribute_SupportsAccumulatorsOnly()
    {
        var attribute = typeof(IsAssignedRegion2<>).GetCustomAttribute<RegionAttribute>();

        Assert.That(
            attribute?.SupportedBorderControlPoints,
            Is.EqualTo(ControlPointType.Accumulator)
        );
    }

    [Test]
    public void Update_ValueType_ExecutesPatchAndAddsWarningOnce()
    {
        var warnings = new WarningRecorder();
        var inlays = new List<RecordingInlay<int>>();
        using var region = CreateRegion(inlays, warnings.Add);

        region.Update(0, out var isAssigned);
        region.Update(1, out _);

        Assert.Multiple(() =>
        {
            Assert.That(isAssigned, Is.True);
            Assert.That(inlays.Single().Values, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(warnings.Added, Is.EqualTo(1));
            Assert.That(warnings.Active, Is.EqualTo(1));
        });
    }

    [Test]
    public void Update_ReferenceType_AddsNoWarning()
    {
        var warnings = new WarningRecorder();
        using var region = CreateRegion(new List<RecordingInlay<string>>(), warnings.Add);

        region.Update(null, out _);
        region.Update("a", out _);

        Assert.That(warnings.Added, Is.Zero);
    }

    [Test]
    public void Update_NullableValueTypeNull_DoesNotExecutePatchAndAddsNoWarning()
    {
        var warnings = new WarningRecorder();
        var inlays = new List<RecordingInlay<int?>>();
        using var region = CreateRegion(inlays, warnings.Add);

        region.Update(null, out var isAssigned);

        Assert.Multiple(() =>
        {
            Assert.That(isAssigned, Is.False);
            Assert.That(inlays, Is.Empty);
            Assert.That(warnings.Added, Is.Zero);
        });
    }

    [Test]
    public void Update_NoRuntimeAvailable_RetriesWarningOnNextUpdate()
    {
        var warnings = new WarningRecorder();
        var runtimeAvailable = false;
        using var region = CreateRegion(
            new List<RecordingInlay<int>>(),
            () => runtimeAvailable ? warnings.Add() : Disposable.Empty
        );

        region.Update(0, out _);
        runtimeAvailable = true;
        region.Update(0, out _);

        Assert.That(warnings.Added, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_WithActiveWarning_RemovesWarning()
    {
        var warnings = new WarningRecorder();
        var region = CreateRegion(new List<RecordingInlay<int>>(), warnings.Add);
        region.Update(0, out _);

        region.Dispose();

        Assert.That(warnings.Active, Is.Zero);
    }

    [Test]
    public void RetrieveOutput_AccumulatorPatchNotExecutedInThisFrame_ReturnsInputValue()
    {
        using var region = new IsAssignedRegion2<string>();
        AsRegion(region)
            .SetPatchInlayFactory(() =>
                new RecordingInlay<string>(inlay =>
                    AsRegion(region).AcknowledgeOutput(AccumulatorOutput, inlay, 2f)
                )
            );
        AsRegion(region).AcknowledgeInput(AccumulatorInput, 1f);
        region.Update("a", out _);

        AsRegion(region).AcknowledgeInput(AccumulatorInput, 3f);
        region.Update(null, out _);
        AsRegion(region).RetrieveOutput(AccumulatorOutput, out var outerValue);

        Assert.That(outerValue, Is.EqualTo(3f));
    }

    private static IRegion<IIsAssignedInlay<T>> AsRegion<T>(IsAssignedRegion2<T> region) => region;

    private static IsAssignedRegion2<T> CreateRegion<T>(
        List<RecordingInlay<T>> createdInlays,
        Func<IDisposable>? addNotNullableWarning = null
    )
    {
        var region = new IsAssignedRegion2<T>(addNotNullableWarning);
        AsRegion(region)
            .SetPatchInlayFactory(() =>
            {
                var inlay = new RecordingInlay<T>(_ => { });
                createdInlays.Add(inlay);
                return inlay;
            });
        return region;
    }

    private sealed class RecordingInlay<T>(Action<RecordingInlay<T>> body) : IIsAssignedInlay<T>
    {
        public List<T> Values { get; } = new();

        public void Update(T value)
        {
            Values.Add(value);
            body(this);
        }
    }

    private sealed class WarningRecorder
    {
        public int Added { get; private set; }

        public int Active { get; private set; }

        public IDisposable Add()
        {
            Added++;
            Active++;
            return Disposable.Create(() => Active--);
        }
    }
}
