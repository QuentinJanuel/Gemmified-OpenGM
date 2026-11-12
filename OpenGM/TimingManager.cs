using OpenGM.IO;
using OpenTK.Graphics.OpenGL4;
using System.Diagnostics;

namespace OpenGM;

/// <summary>
/// for cpp:
/// fps = count frames in a second like here. so +1 per new frame. includes the waiting at the end of the frame.
/// fps_real = times just the frame, without waiting and without counting. also without window event handling like inputs.
/// </summary>
public static class TimingManager
{
    public static double FPS;
    public static double FPSReal;
    public static double DeltaTime;

    public static bool DebugTime;

    private static int _frameCounter;
    private static Stopwatch _oneSecondStopwatch = new();
    private static Stopwatch _frameStopwatch = new();
    private static int _query;

    public static void Initialize()
    {
        FPS = Entry.GameSpeed;
        _oneSecondStopwatch.Restart();
    }

    public static void BeginFrame(double dt)
    {
        // GML's delta_time is documented (and universally relied upon, e.g. by any GML code that
        // manually divides it by 1000000 to get seconds) as MICROSECONDS since the last step --
        // dt here is FrameEventArgs.Time/the wasm step's seconds, so it has to be converted before
        // being exposed as delta_time (VariableResolver.cs), or any time-based GML math built on
        // that documented unit silently runs ~1,000,000x slower than intended.
        DeltaTime = dt * 1_000_000;

        if (DebugTime)
        {
            if (_query == 0)
            {
                GL.CreateQueries(QueryTarget.TimeElapsed, 1, out _query);
            }

            GL.GetQuery(QueryTarget.TimeElapsed, GetQueryParam.CurrentQuery, out var currentQuery);
            if (currentQuery != 0)
            {
                GL.EndQuery(QueryTarget.TimeElapsed);
            }

            GL.BeginQuery(QueryTarget.TimeElapsed, _query);
        }

        if (_oneSecondStopwatch.Elapsed.TotalSeconds >= 1)
        {
            FPS = _frameCounter;
            _frameCounter = 0;
            _oneSecondStopwatch.Restart();
        }
        else
        {
            _frameCounter++;
        }

        _frameStopwatch.Restart();
    }

    public static void EndFrame()
    {
        _frameStopwatch.Stop();
        FPSReal = 1 / _frameStopwatch.Elapsed.TotalSeconds;

        if (DebugTime)
        {
            GL.GetQuery(QueryTarget.TimeElapsed, GetQueryParam.CurrentQuery, out var currentQuery);
            if (currentQuery != 0)
            {
                var cpuTime = _frameStopwatch.Elapsed;

                GL.EndQuery(QueryTarget.TimeElapsed);

                GL.GetQueryObject(_query, GetQueryObjectParam.QueryResult, out int nanoseconds);
                var gpuTime = TimeSpan.FromSeconds(nanoseconds * 1e-9);

                DebugLog.Log($"fps = {FPS} fps\tfps_real = {FPSReal:N} fps\t\tcpu time = {cpuTime.TotalMilliseconds:N} ms\tgpu time = {gpuTime.TotalMilliseconds:N} ms");
            }
        }
    }
}
