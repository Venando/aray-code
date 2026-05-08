namespace OpenClawPTT.Tests;

using OpenClawPTT;
using System;
using System.ComponentModel;
using Xunit;
using NAudio;

/// <summary>
/// Stability tests for AudioRecorder.
///
/// These tests verify that AudioRecorder handles edge cases gracefully:
/// - Missing audio tools (sox/arecord on Linux/macOS)
/// - Double-start (idempotency)
/// - Stop without start
/// - Dispose without stop
/// - Invalid/edge-case parameters
/// </summary>
[Collection("AudioRecorder")]
public class AudioRecorderStabilityTests
{
    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording with missing sox/arecord → throws (tool not found)
    // On Linux, Process.Start throws Win32Exception when the executable is missing.
    // On Windows, NAudio is used instead and this test is not relevant.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StartRecording_WhenSoxAndArecordMissing_Throws()
    {
        if (OperatingSystem.IsWindows())
            return; // sox/arecord are Unix-only; Windows uses NAudio

        using var recorder = new AudioRecorder();
        Assert.Throws<Win32Exception>(() => recorder.StartRecording());
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording called twice → second call returns without leak
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StartRecording_WhenAlreadyRecording_IsIdempotent()
    {
        using var recorder = new AudioRecorder();

        try
        {
            recorder.StartRecording();
        }
        catch (Win32Exception)
        {
            // Linux without audio tools — second-call test is not applicable on this platform
            return;
        }

        // If we got here, the platform started recording successfully.
        // Second call must not throw and must not change state.
        var before = recorder.IsRecording;
        recorder.StartRecording(); // must not throw
        Assert.Equal(before, recorder.IsRecording);

        recorder.StopRecording();
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StopRecording without StartRecording → returns empty byte[] without crash
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StopRecording_WhenNotRecording_ReturnsEmptyByteArray()
    {
        using var recorder = new AudioRecorder();

        var result = recorder.StopRecording();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: Dispose without StopRecording → cleans up without throwing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_WhenRecording_CleansUpWithoutThrowing()
    {
        using var recorder = new AudioRecorder();

        try
        {
            recorder.StartRecording();
        }
        catch (Win32Exception)
        {
            // Linux without audio tools — dispose still must not throw
            recorder.Dispose();
            return;
        }

        // If recording started, dispose without prior StopRecording must not throw
        recorder.Dispose(); // first dispose
        recorder.Dispose(); // second dispose — must be idempotent
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording with default config → uses defaults without throwing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_WithDefaults_CreatesRecorderWithoutThrowing()
    {
        // The default constructor must not throw.
        using var recorder = new AudioRecorder();

        Assert.False(recorder.IsRecording);
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording with zero sample rate → handled gracefully
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StartRecording_WithZeroSampleRate_HandledGracefully()
    {
        using var recorder = new AudioRecorder(sampleRate: 0);

        try
        {
            recorder.StartRecording();
        }
        catch (Exception ex) when (
            ex is Win32Exception ||
            ex is InvalidOperationException ||
            ex is ArgumentException ||
            ex is MmException)
        {
            // Expected: either the tool rejects 0 sample rate, or NAudio throws.
            return;
        }

        recorder.StopRecording();
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording with negative sample rate → handled gracefully
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StartRecording_WithNegativeSampleRate_HandledGracefully()
    {
        using var recorder = new AudioRecorder(sampleRate: -1);

        try
        {
            recorder.StartRecording();
        }
        catch (Exception ex) when (
            ex is Win32Exception ||
            ex is InvalidOperationException ||
            ex is ArgumentException ||
            ex is MmException)
        {
            return;
        }

        recorder.StopRecording();
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: StartRecording with very large maxSeconds → handled gracefully
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StartRecording_WithVeryLargeMaxSeconds_IsHandledGracefully()
    {
        // 86400 seconds = 24 hours — extreme but should not crash.
        using var recorder = new AudioRecorder(maxSeconds: 86400);

        try
        {
            recorder.StartRecording();
        }
        catch (Win32Exception)
        {
            // Linux without audio tools — expected
            return;
        }

        recorder.StopRecording();
    }

    // ═══════════════════════════════════════════════════════════════
    // TEST: Multiple record cycles → no resource leaks
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleRecordCycles_NoResourceLeaks()
    {
        using var recorder = new AudioRecorder();

        for (int i = 0; i < 3; i++)
        {
            try
            {
                recorder.StartRecording();
            }
            catch (Win32Exception)
            {
                // Linux without audio tools — can't test cycles on this platform
                return;
            }

            var bytes = recorder.StopRecording();
            Assert.NotNull(bytes);
        }
    }

}

/// <summary>
/// Marks this collection as non-parallelizable because AudioRecorder touches
/// NAudio's native wave-in APIs that cannot handle concurrent access to the
/// same audio device from multiple threads/processes.
/// </summary>
[CollectionDefinition("AudioRecorder", DisableParallelization = true)]
public class AudioRecorderCollection : ICollectionFixture<object>
{
    // No fixture data needed — collection is used solely to disable parallelization
    // because AudioRecorder's NAudio wave-in handle cannot be safely shared across
    // concurrent test threads.
}
