using System.Numerics;
using SDL;
using static SDL.SDL3;

namespace Mdk.Engine.Platform;

/// <summary>Whether the phone's gyroscope turns the view (a VR viewer: the head looks around).</summary>
public enum HeadTracking { Off, On }

/// <summary>The device's gyroscope (SDL's sensors; phones), its turns added to <see cref="Input"/>'s
/// look through <see cref="HeadLook"/>. Without a gyroscope it does nothing.</summary>
internal sealed unsafe class HeadTracker : IDisposable
{
    private const float NanosecondsPerSecond = 1e9f;
    /// <summary>A longer gap between readings (a pause) turns nothing.</summary>
    private const float MaxStep = 0.1f;
    private const int Axes = 3;

    private SDL_Sensor* _gyro;
    private ulong _lastReading;

    /// <summary>Opens the gyroscope, or closes it.</summary>
    public void Set(HeadTracking tracking)
    {
        if ((tracking == HeadTracking.On) == (_gyro != null))
        {
            return;
        }

        Close();
        if (tracking == HeadTracking.Off || !SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_SENSOR))
        {
            return;
        }

        int count;
        var sensors = SDL_GetSensors(&count);
        for (var i = 0; i < count && _gyro == null; i++)
        {
            if (SDL_GetSensorTypeForID(sensors[i]) == SDL_SensorType.SDL_SENSOR_GYRO)
            {
                _gyro = SDL_OpenSensor(sensors[i]);
            }
        }

        SDL_free(sensors);
    }

    /// <summary>Handles a sensor event (false for the others): the head's turn since the last reading,
    /// while <paramref name="looking"/> (play), for the window's way up.</summary>
    public bool Event(in SDL_Event e, Input input, bool looking, SDL_Window* window)
    {
        if ((SDL_EventType)e.type != SDL_EventType.SDL_EVENT_SENSOR_UPDATE)
        {
            return false;
        }

        if (_gyro == null)
        {
            return true;
        }

        // The sensor's own clock when it has one, else the event's.
        var reading = e.sensor.sensor_timestamp != 0 ? e.sensor.sensor_timestamp : e.sensor.timestamp;
        var seconds = _lastReading == 0 ? 0f : MathF.Min((reading - _lastReading) / NanosecondsPerSecond, MaxStep);
        _lastReading = reading;
        if (!looking || seconds <= 0f)
        {
            return true;
        }

        var data = e.sensor.data;
        var rate = new Vector3(data[0], data[1], data[Axes - 1]);
        input.AddLook(HeadLook.FromGyro(rate, seconds, Turn(window)));
        return true;
    }

    private static ScreenTurn Turn(SDL_Window* window) =>
        SDL_GetCurrentDisplayOrientation(SDL_GetDisplayForWindow(window)) == SDL_DisplayOrientation.SDL_ORIENTATION_LANDSCAPE_FLIPPED
            ? ScreenTurn.LandscapeFlipped
            : ScreenTurn.Landscape;

    private void Close()
    {
        if (_gyro == null)
        {
            return;
        }

        SDL_CloseSensor(_gyro);
        _gyro = null;
        _lastReading = 0;
    }

    public void Dispose() => Close();
}
