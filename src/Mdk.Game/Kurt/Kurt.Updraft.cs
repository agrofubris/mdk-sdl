namespace Mdk.Game.Kurt;

/// <summary>Kurt in the fans' updrafts (damp_gravity, kurt.gd _update_updraft): inside a fan's box
/// the fan sets his vertical speed and opens his chute, with the FAN sound looping; out of them he
/// can't rise faster than 40 u/s.</summary>
public sealed partial class Kurt
{
    /// <summary>Out of an updraft Kurt rises at most this fast.</summary>
    public const float UpdraftExitSpeed = 40f;
    private const string FanSound = "FAN";

    /// <summary>The fans (set by the scripts runtime): (vertical speed, seconds) → the new vertical
    /// speed, or NaN outside them.</summary>
    public Func<float, float, float>? Updraft;

    private int _fanVoice;

    /// <summary>Whether a fan holds Kurt.</summary>
    public bool InUpdraft { get; private set; }

    private void UpdateUpdraft(float delta)
    {
        var vz = Updraft != null && Health > 0 ? Updraft(VerticalSpeed, delta) : float.NaN;
        if (float.IsNaN(vz))
        {
            mixer.StopVoice(_fanVoice);
            _fanVoice = 0;
            InUpdraft = false;
            if (Current != State.Knocked && VerticalSpeed > UpdraftExitSpeed)
            {
                VerticalSpeed = UpdraftExitSpeed;
            }

            return;
        }

        VerticalSpeed = vz;
        if (!ChuteOpen)
        {
            ChuteOpen = true;
            mixer.Play("CHUTEOUT");
        }

        if (!InUpdraft)
        {
            InUpdraft = true;
            _fanVoice = mixer.PlayLooped(FanSound);
        }
    }
}
