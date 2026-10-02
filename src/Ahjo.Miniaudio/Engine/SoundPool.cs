using System.Numerics;

namespace Ahjo.Miniaudio;

/// <summary>
/// A fixed set of voices over one <see cref="SoundAsset"/>, for effects that
/// overlap — footsteps, gunshots, impacts. <see cref="Play()"/> takes an idle
/// voice, or steals the least recently played one, and allocates nothing.
/// </summary>
/// <remarks>
/// The voices are ordinary <see cref="Sound"/>s on the engine; its
/// <see cref="AudioEngine.Dispose"/> disposes them, and so does this pool's.
/// </remarks>
public sealed class SoundPool : IDisposable
{
    private readonly Sound[] _voices;
    private int _next;

    private SoundPool(Sound[] voices) => _voices = voices;

    /// <summary>Creates <paramref name="voices"/> stopped sounds over <paramref name="asset"/>.</summary>
    /// <exception cref="MiniaudioException">miniaudio could not initialize a voice; none are left behind.</exception>
    public static SoundPool Create(AudioEngine engine, SoundAsset asset, int voices, in SoundDescription description = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(voices, 1);
        var sounds = new Sound[voices];
        var created = 0;
        try
        {
            for (; created < voices; created++)
            {
                sounds[created] = Sound.Create(engine, asset, description);
            }
        }
        catch
        {
            for (var i = 0; i < created; i++)
            {
                sounds[i].Dispose();
            }

            throw;
        }

        return new SoundPool(sounds);
    }

    /// <summary>The pool's voices.</summary>
    public ReadOnlySpan<Sound> Voices => _voices;

    /// <summary>Plays the effect from the start on a free voice and returns that voice.</summary>
    /// <remarks>The returned voice may be stolen by a later <see cref="Play()"/>; don't hold on to it past the effect.</remarks>
    public Sound Play()
    {
        var voice = Acquire();
        voice.Play();
        return voice;
    }

    /// <summary>Plays the effect at <paramref name="position"/> (the pool's description must be <see cref="SoundDescription.Spatialized"/>).</summary>
    /// <inheritdoc cref="Play()" path="/remarks"/>
    public Sound Play(Vector3 position)
    {
        var voice = Acquire();
        voice.Position = position;
        voice.Play();
        return voice;
    }

    /// <summary>Stops every voice.</summary>
    public void Stop()
    {
        foreach (var voice in _voices)
        {
            voice.Stop();
        }
    }

    // _next is always the voice after the last one played, so scanning from
    // it finds the least recently played idle voice first — and when none is
    // idle, _next itself is the least recently played one to steal.
    private Sound Acquire()
    {
        var count = _voices.Length;
        for (var i = 0; i < count; i++)
        {
            var index = (_next + i) % count;
            if (!_voices[index].IsPlaying)
            {
                _next = (index + 1) % count;
                return _voices[index];
            }
        }

        var stolen = _voices[_next];
        _next = (_next + 1) % count;
        return stolen;
    }

    /// <summary>Disposes every voice.</summary>
    public void Dispose()
    {
        foreach (var voice in _voices)
        {
            voice.Dispose();
        }
    }
}
