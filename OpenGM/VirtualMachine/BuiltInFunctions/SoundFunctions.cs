using OpenGM.IO;
using OpenTK.Audio.OpenAL;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;

namespace OpenGM.VirtualMachine.BuiltInFunctions
{
    public static class SoundFunctions
    {
        // MCI_command

        [GMLFunction("audio_listener_position")]
        public static object? audio_listener_position(object?[] args)
        {
            var x = args[0].Conv<float>();
            var y = args[1].Conv<float>();
            var z = args[2].Conv<float>();

            AudioManager.Listener.Position = new Vector3(x, y, z);
            return null;
        }

        [GMLFunction("audio_listener_velocity")]
        public static object? audio_listener_velocity(object?[] args)
        {
            var vx = args[0].Conv<float>();
            var vy = args[1].Conv<float>();
            var vz = args[2].Conv<float>();

            AudioManager.Listener.Velocity = new Vector3(vx, vy, vz);
            return null;
        }

        [GMLFunction("audio_listener_orientation")]
        public static object? audio_listener_orientation(object?[] args)
        {
            var lookat_x = args[0].Conv<float>();
            var lookat_y = args[1].Conv<float>();
            var lookat_z = args[2].Conv<float>();
            var up_x = args[3].Conv<float>();
            var up_y = args[4].Conv<float>();
            var up_z = args[5].Conv<float>();

            AudioManager.Listener.At = new Vector3(lookat_x, lookat_y, lookat_z);
            AudioManager.Listener.Up = new Vector3(up_x, up_y, up_z);
            return null;
        }

        [GMLFunction("audio_emitter_position")]
        public static object? audio_emitter_position(object?[] args)
        {
            var id = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();
            var z = args[3].Conv<float>();

            var emitter = AudioManager.GetAudioEmitter(id);
            emitter.Position = new(x, y, z);
            return null;
        }

        [GMLFunction("audio_emitter_velocity")]
        public static object? audio_emitter_velocity(object?[] args)
        {
            var id = args[0].Conv<int>();
            var vx = args[1].Conv<float>();
            var vy = args[2].Conv<float>();
            var vz = args[3].Conv<float>();

            var emitter = AudioManager.GetAudioEmitter(id);
            emitter.Velocity = new(vx, vy, vz);
            return null;
        }

        // audio_system

        [GMLFunction("audio_emitter_create")]
        public static object? audio_emitter_create(object?[] args)
        {
            return AudioManager.AudioEmitterCreate();
        }

        [GMLFunction("audio_emitter_free", GMLFunctionFlags.Stub)]
        public static object? audio_emitter_free(object?[] args)
        {
            return null;
        }

        [GMLFunction("audio_play_sound")]
        public static object? audio_play_sound(object?[] args)
        {
            var index = args[0].Conv<int>();
            var priority = args[1].Conv<double>();
            var loop = args[2].Conv<bool>();
            var asset = AudioManager.GetAudioAsset(index);

            if (asset is null)
            {
                DebugLog.LogWarning($"Tried to play nonexistent sound {index}");
                return -1;
            }

            var gain = asset.Gain;
            var offset = asset.Offset;
            var pitch = asset.Pitch;
            var listener_mask = 0; // TODO : work out what the hell this is for
            if (args.Length > 3)
            {
                gain = args[3].Conv<double>();
            }

            if (args.Length > 4)
            {
                offset = args[4].Conv<double>();
            }

            if (args.Length > 5)
            {
                pitch = args[5].Conv<double>();
            }

            if (args.Length > 6)
            {
                listener_mask = args[6].Conv<int>();
            }

            var ret = AudioManager.audio_play_sound(index, priority, loop, gain, offset, pitch);
            return ret;
        }

        [GMLFunction("audio_play_sound_on")]
        public static object? audio_play_sound_on(object?[] args)
        {
            var emitter = args[0].Conv<int>();
            var sound = args[1].Conv<int>();
            var loop = args[2].Conv<bool>();
            var priority = args[3].Conv<double>();

            var gain = 1f;
            var offset = 0f;
            var pitch = 1f;
            var listener_mask = 1;

            if (args.Length > 4)
            {
                gain = args[4].Conv<float>();
            }

            if (args.Length > 5)
            {
                offset = args[5].Conv<float>();
            }

            if (args.Length > 6)
            {
                pitch = args[6].Conv<float>();
            }

            if (args.Length > 7)
            {
                listener_mask = args[7].Conv<int>();
            }

            return AudioManager.PlaySoundOnEmitter(AudioManager.GetAudioEmitter(emitter), sound, priority, loop, gain, offset, pitch, listener_mask);
        }

        // Convenience wrapper combining a one-shot emitter (position + falloff) with playback, so a
        // game doesn't have to manually audio_emitter_create/position/free just to play one
        // positional sound. Falloff is applied to a throwaway emitter, not AudioManager's normal
        // reusable emitter pool, since the caller never gets a handle to free it themselves.
        [GMLFunction("audio_play_sound_at")]
        public static object? audio_play_sound_at(object?[] args)
        {
            var index = args[0].Conv<int>();
            var x = args[1].Conv<float>();
            var y = args[2].Conv<float>();
            var z = args[3].Conv<float>();
            var falloffRef = args[4].Conv<float>();
            var falloffMax = args[5].Conv<float>();
            var falloffFactor = args[6].Conv<float>();
            var loop = args[7].Conv<bool>();
            var priority = args[8].Conv<double>();

            var asset = AudioManager.GetAudioAsset(index);
            var gain = asset?.Gain ?? 1;
            var offset = asset?.Offset ?? 0;
            var pitch = asset?.Pitch ?? 1;
            var listenerMask = 0;

            if (args.Length > 9)
                gain = args[9].Conv<double>();
            if (args.Length > 10)
                offset = args[10].Conv<double>();
            if (args.Length > 11)
                pitch = args[11].Conv<double>();
            if (args.Length > 12)
                listenerMask = args[12].Conv<int>();

            var emitterId = AudioManager.AudioEmitterCreate();
            var emitter = AudioManager.GetAudioEmitter(emitterId);
            emitter.Position = new Vector3(x, y, z);
            emitter.FalloffRef = falloffRef;
            emitter.FalloffMax = falloffMax;
            emitter.FalloffFac = falloffFactor;

            return AudioManager.PlaySoundOnEmitter(emitter, index, priority, loop, gain, offset, pitch, listenerMask);
        }

        [GMLFunction("audio_falloff_set_model")]
        public static object? audio_falloff_set_model(object?[] args)
        {
            var model = (FalloffModel)args[0].Conv<int>();

            var alModel = model switch
            {
                FalloffModel.NONE => ALDistanceModel.None,
                FalloffModel.INVERSE_DISTANCE => ALDistanceModel.InverseDistance,
                FalloffModel.INVERSE_DISTANCE_CLAMPED => ALDistanceModel.InverseDistanceClamped,
                FalloffModel.LINEAR_DISTANCE => ALDistanceModel.LinearDistance,
                FalloffModel.LINEAR_DISTANCE_CLAMPED => ALDistanceModel.LinearDistanceClamped,
                FalloffModel.EXPONENT_DISTANCE => ALDistanceModel.ExponentDistance,
                FalloffModel.EXPONENT_DISTANCE_CLAMPED => ALDistanceModel.ExponentDistanceClamped,
                FalloffModel.INVERSE_DISTANCE_SCALED => throw new NotImplementedException(),
                FalloffModel.EXPONENT_DISTANCE_SCALED => throw new NotImplementedException(),
                _ => throw new ArgumentOutOfRangeException()
            };

            AL.DistanceModel(alModel);
            AudioManager.CheckALError();
            return null;
        }

        [GMLFunction("audio_stop_sound")]
        public static object? audio_stop_sound(object?[] args)
        {
            var id = args[0].Conv<int>();

            if (id < GMConstants.BASE_SOUND_INDEX)
            {
                foreach (var item in AudioManager.GetAudioInstances(id))
                {
                    AL.SourceStop(item.Source);
                    AudioManager.CheckALError();
                }
            }
            else
            {
                var soundAsset = AudioManager.GetAudioInstance(id);
                if (soundAsset == null)
                {
                    //DebugLog.LogWarning($"trying to stop sound {id} which does not exist.\n" +
                    //    $"it was probably either done playing or already stopped");
                    return null;
                }
                AL.SourceStop(soundAsset.Source);
                AudioManager.CheckALError();
            }
            AudioManager.Update(); // hack: deletes the sources. maybe make official stop and delete function

            return null;
        }

        [GMLFunction("audio_pause_sound")]
        public static object? audio_pause_sound(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                foreach (var item in AudioManager.GetAudioInstances(index))
                {
                    AL.SourcePause(item.Source);
                    AudioManager.CheckALError();
                }
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                if (instance != null)
                {
                    AL.SourcePause(instance.Source);
                    AudioManager.CheckALError();
                }

            }

            return null;
        }

        [GMLFunction("audio_resume_sound")]
        public static object? audio_resume_sound(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                foreach (var item in AudioManager.GetAudioInstances(index))
                {
                    ResumeIfNotPlaying(item);
                }
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                if (instance != null)
                {
                    ResumeIfNotPlaying(instance);
                }
            }

            return null;
        }

        [GMLFunction("audio_pause_all")]
        public static object? audio_pause_all(object?[] args)
        {
            foreach (var item in AudioManager.GetAllAudioInstances())
            {
                AL.SourcePause(item.Source);
                AudioManager.CheckALError();
            }

            return null;
        }

        [GMLFunction("audio_resume_all")]
        public static object? audio_resume_all(object?[] args)
        {
            foreach (var item in AudioManager.GetAllAudioInstances())
            {
                ResumeIfNotPlaying(item);
            }

            return null;
        }

        /// <summary>
        /// A source that's merely paused (audio_pause_sound) is still tracked as a live
        /// AudioInstance -- only a genuinely stopped/finished source gets reclaimed by
        /// AudioManager.Update(). "Instance exists" therefore isn't enough to tell playing and
        /// paused apart; both audio_is_playing and audio_is_paused need the real source state.
        /// </summary>
        private static bool InstanceHasState(AudioInstance instance, ALSourceState wanted)
        {
            var state = (ALSourceState)AL.GetSource(instance.Source, ALGetSourcei.SourceState);
            AudioManager.CheckALError();
            return state == wanted;
        }

        /// <summary>
        /// Real GM's audio_resume_sound/audio_resume_all docs: "If the sound is already playing
        /// this function does nothing." AL.SourcePlay on an already-playing source restarts it from
        /// the beginning instead (both per the OpenAL spec and this project's own webaudio.js
        /// backend, which always tears down and starts a fresh node) -- guarding here matches real
        /// GM's no-op-if-already-playing behavior instead of restarting a sound that was never
        /// actually paused (e.g. calling audio_resume_sound defensively/unconditionally, which real
        /// games do -- see YoYoYo_Engine's restart-room key handler).
        /// </summary>
        private static void ResumeIfNotPlaying(AudioInstance instance)
        {
            if (InstanceHasState(instance, ALSourceState.Playing))
            {
                return;
            }

            AL.SourcePlay(instance.Source);
            AudioManager.CheckALError();
        }

        [GMLFunction("audio_is_playing")]
        public static object? audio_is_playing(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                return AudioManager.GetAudioInstances(index).Any(item => InstanceHasState(item, ALSourceState.Playing));
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                return instance != null && InstanceHasState(instance, ALSourceState.Playing);
            }
        }

        [GMLFunction("audio_is_paused")]
        public static object? audio_is_paused(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                return AudioManager.GetAudioInstances(index).Any(item => InstanceHasState(item, ALSourceState.Paused));
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                return instance != null && InstanceHasState(instance, ALSourceState.Paused);
            }
        }

        [GMLFunction("audio_exists")]
        public static object? audio_exists(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                return AudioManager.GetAudioInstances(index).Any();
            }
            else
            {
                return AudioManager.GetAudioInstance(index) != null;
            }
        }

        // This engine's audio system is either fully initialized (AudioManager.Init, called from
        // Entry.LoadGame before any GML runs) or the game hasn't started yet -- there's no
        // "hardware unavailable" failure mode to report, unlike real DirectSound/XAudio2 hardware
        // enumeration.
        [GMLFunction("audio_system_is_available")]
        public static object? audio_system_is_available(object?[] args) => true;

        [GMLFunction("audio_sound_is_playable")]
        public static object? audio_sound_is_playable(object?[] args)
        {
            var index = args[0].Conv<int>();
            return AudioManager.GetAudioAsset(index) != null;
        }

        // Older, single-argument predecessor of audio_set_master_gain(listener_index, gain) below
        // -- same underlying global gain, no per-listener index.
        [GMLFunction("audio_master_gain")]
        public static object? audio_master_gain(object?[] args)
        {
            var gain = Math.Max(0, args[0].Conv<double>());
            AL.Listener(ALListenerf.Gain, (float)gain);
            AudioManager.CheckALError();
            return null;
        }

        [GMLFunction("audio_emitter_exists")]
        public static object? audio_emitter_exists(object?[] args)
        {
            var id = args[0].Conv<int>();

            return AudioManager.AudioEmitterExists(id);
        }

        // Every sound resource is loaded through the same AudioManager path regardless of whether a
        // game authored it for the legacy sound_* system or the modern audio_* one -- there's no
        // real distinction to report, so this always answers audio_new_system (1).
        [GMLFunction("audio_get_type")]
        public static object? audio_get_type(object?[] args) => 1;

        [GMLFunction("audio_emitter_gain")]
        public static object? audio_emitter_gain(object?[] args)
        {
            var id = args[0].Conv<int>();
            var gain = args[1].Conv<float>();

            var emitter = AudioManager.GetAudioEmitter(id);
            emitter.Gain = gain;

            foreach (var sound in emitter.AttachedSounds)
            {
                AL.Source(sound.Source, ALSourcef.Gain, gain);
                AudioManager.CheckALError();
            }

            return null;
        }

        [GMLFunction("audio_emitter_pitch")]
        public static object? audio_emitter_pitch(object?[] args)
        {
            var id = args[0].Conv<int>();
            var pitch = args[1].Conv<float>();

            var emitter = AudioManager.GetAudioEmitter(id);
            emitter.Pitch = pitch;

            foreach (var sound in emitter.AttachedSounds)
            {
                // TODO : is this wrong? see how pitch is calculated
                AL.Source(sound.Source, ALSourcef.Pitch, pitch);
                AudioManager.CheckALError();
            }

            return null;
        }

        [GMLFunction("audio_emitter_falloff")]
        public static object? audio_emitter_falloff(object?[] args)
        {
            var id = args[0].Conv<int>();
            var falloff_ref = args[1].Conv<float>();
            var falloff_max = args[2].Conv<float>();
            var falloff_factor = args[3].Conv<float>();

            var emitter = AudioManager.GetAudioEmitter(id);
            emitter.FalloffRef = falloff_ref;
            emitter.FalloffMax = falloff_max;
            emitter.FalloffFac = falloff_factor;

            foreach (var sound in emitter.AttachedSounds)
            {
                AL.Source(sound.Source, ALSourcef.ReferenceDistance, falloff_ref);
                AudioManager.CheckALError();
                AL.Source(sound.Source, ALSourcef.MaxDistance, falloff_max);
                AudioManager.CheckALError();
                AL.Source(sound.Source, ALSourcef.RolloffFactor, falloff_factor);
                AudioManager.CheckALError();
            }

            return null;
        }

        [GMLFunction("audio_channel_num")]
        public static object? audio_channel_num(object?[] args)
        {
            var num = args[0].Conv<int>();

            AudioManager.StopAllAudio();
            AudioManager.AudioChannelNum = num;
            return null;
        }

        // audio_play_music
        // audio_stop_music
        // audio_pause_music
        // audio_resume_music
        // audio_music_is_playing
        // audio_music_gain

        [GMLFunction("audio_sound_gain")]
        public static object? audio_sound_gain(object?[] args)
        {
            var index = args[0].Conv<int>();
            var volume = args[1].Conv<double>();
            var time = args[2].Conv<double>();

            if (index < 0)
            {
                return null;
            }

            if (index >= GMConstants.BASE_SOUND_INDEX)
            {
                // instance id
                var soundAsset = AudioManager.GetAudioInstance(index);
                if (soundAsset == null)
                {
                    return null;
                }

                AudioManager.ChangeGain(soundAsset, volume, time);
            }
            else
            {
                // sound asset index
                AudioManager.SetAssetGain(index, volume);

                foreach (var item in AudioManager.GetAudioInstances(index))
                {
                    AudioManager.ChangeGain(item, volume, time);
                }
            }

            return null;
        }

        [GMLFunction("audio_sound_pitch")]
        public static object? audio_sound_pitch(object?[] args)
        {
            var index = args[0].Conv<int>();
            var pitch = args[1].Conv<double>();

            pitch = Math.Clamp(pitch, 1.0 / 256.0, 256.0);

            if (index >= GMConstants.BASE_SOUND_INDEX)
            {
                // instance id
                var soundAsset = AudioManager.GetAudioInstance(index);
                if (soundAsset == null)
                {
                    return null;
                }

                AL.Source(soundAsset.Source, ALSourcef.Pitch, (float)pitch);
                AudioManager.CheckALError();
            }
            else
            {
                // sound asset index
                AudioManager.SetAssetPitch(index, pitch);

                foreach (var item in AudioManager.GetAudioInstances(index))
                {
                    AL.Source(item.Source, ALSourcef.Pitch, (float)pitch);
                    AudioManager.CheckALError();
                }
            }

            return null;
        }

        [GMLFunction("audio_stop_all")]
        public static object? audio_stop_all(object?[] args)
        {
            AudioManager.StopAllAudio();
            return 0; // "returns nothing" is 0 in GameMaker, not undefined -- see move_towards_point
        }

        [GMLFunction("audio_sound_length")]
        public static object audio_sound_length(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                var asset = AudioManager.GetAudioAsset(index);
                if (asset != null)
                {
                    return AudioManager.GetClipLength(asset);
                }
                return -1;
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                if (instance != null)
                {
                    return AudioManager.GetClipLength(instance.Asset);
                }
                return -1;
            }
        }

        // ...

        [GMLFunction("audio_set_master_gain")]
        public static object? audio_set_master_gain(object?[] args)
        {
            var listenerIndex = args[0].Conv<double>(); // deltarune doesnt use other listeners rn so i dont care
            var gain = args[1].Conv<double>();

            gain = Math.Max(0, gain);

            AL.Listener(ALListenerf.Gain, (float)gain);
            AudioManager.CheckALError();
            return null;
        }

        [GMLFunction("audio_get_master_gain")]
        public static object? audio_get_master_gain(object?[] args)
        {
            AL.GetListener(ALListenerf.Gain, out var gain);
            AudioManager.CheckALError();
            return (double)gain;
        }

        [GMLFunction("audio_sound_get_gain")]
        public static object? audio_sound_get_gain(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index >= GMConstants.BASE_SOUND_INDEX)
            {
                var instance = AudioManager.GetAudioInstance(index);

                if (instance == null)
                {
                    return 0;
                }

                AL.GetSource(instance.Source, ALSourcef.Gain, out var gain);
                AudioManager.CheckALError();
                return gain;
            }
            else
            {
                var asset = AudioManager.GetAudioAsset(index);
                return asset?.Gain ?? 0;
            }
        }

        [GMLFunction("audio_sound_get_pitch")]
        public static object? audio_sound_get_pitch(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index >= GMConstants.BASE_SOUND_INDEX)
            {
                // instance id
                var soundAsset = AudioManager.GetAudioInstance(index);
                if (soundAsset == null)
                {
                    return null;
                }

                var pitch = (double)AL.GetSource(soundAsset.Source, ALSourcef.Pitch);
                AudioManager.CheckALError();
                return pitch;
            }
            else
            {
                // sound asset index
                return AudioManager.GetAssetPitch(index);
            }
        }

        [GMLFunction("audio_get_name")]
        public static object? audio_get_name(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index >= GMConstants.BASE_SOUND_INDEX)
            {
                var instance = AudioManager.GetAudioInstance(index);

                return instance == null ? "<undefined>" : instance.Asset.Name;
            }

            var asset = AudioManager.GetAudioAsset(index);
            return asset == null ? "<undefined>" : asset.Name;
        }

        [GMLFunction("audio_sound_set_track_position")]
        public static object? audio_sound_set_track_position(object?[] args)
        {
            var index = args[0].Conv<int>();
            var time = args[1].Conv<double>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                AudioManager.SetAssetOffset(index, time);

                // unlike gain and pitch, this doesnt change currently playing instances
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                if (instance != null)
                {
                    AL.Source(instance.Source, ALSourcef.SecOffset, (float)time);
                    AudioManager.CheckALError();
                }
            }

            return null;
        }

        [GMLFunction("audio_sound_get_track_position")]
        public static object audio_sound_get_track_position(object?[] args)
        {
            var index = args[0].Conv<int>();

            if (index < GMConstants.BASE_SOUND_INDEX)
            {
                return AudioManager.GetAssetOffset(index);

                // unlike gain and pitch, this doesnt change currently playing instances
            }
            else
            {
                var instance = AudioManager.GetAudioInstance(index);
                if (instance != null)
                {
                    var offset = AL.GetSource(instance!.Source, ALSourcef.SecOffset);
                    AudioManager.CheckALError();
                    return offset;
                }
                return 0;
            }
        }

        [GMLFunction("audio_group_load", GMLFunctionFlags.Stub)]
        public static object audio_group_load(object?[] args)
        {
            // TODO : actually implement this properly? DELTARUNITY doesnt use audio groups or any GM storage files (yet?)
            return true;
        }

        [GMLFunction("audio_group_unload", GMLFunctionFlags.Stub)]
        public static object? audio_group_unload(object?[] args)
        {
            // TODO : actually implement this properly? DELTARUNITY doesnt use audio groups or any GM storage files (yet?)
            return true;
        }

        [GMLFunction("audio_group_is_loaded", GMLFunctionFlags.Stub)]
        public static object audio_group_is_loaded(object?[] args)
        {
            // TODO : actually implement this properly? DELTARUNITY doesnt use audio groups or any GM storage files (yet?)
            return true;
        }

        [GMLFunction("audio_group_load_progress", GMLFunctionFlags.Stub)]
        public static object audio_group_load_progress(object?[] args)
        {
            return 100;
        }

        // Every group is treated as always-loaded (see audio_group_load's own TODO), so there's no
        // real per-group name registry -- "group<N>" is a stable, distinguishable placeholder.
        [GMLFunction("audio_group_name", GMLFunctionFlags.Stub)]
        public static object? audio_group_name(object?[] args) => $"group{args[0].Conv<int>()}";

        [GMLFunction("audio_group_stop_all", GMLFunctionFlags.Stub)]
        public static object? audio_group_stop_all(object?[] args)
        {
            // TODO : actually implement this properly once audio groups track their own sounds
            return null;
        }

        [GMLFunction("audio_group_set_gain", GMLFunctionFlags.Stub)]
        public static object? audio_group_set_gain(object?[] args)
        {
            // TODO : actually implement this properly? DELTARUNITY doesnt use audio groups or any GM storage files (yet?)
            return null;
        }

        /// <summary>
        /// Converts a slice of a GML buffer's raw bytes (already-decoded PCM, unlike sound_add/
        /// LoadSounds which decode a compressed file format) into interleaved float samples, per
        /// buffer_get_type's own encoding -- audio_create_buffer_sound's own "format" argument.
        /// </summary>
        private static float[] BufferBytesToFloatSamples(byte[] data, int byteOffset, int byteLength, BufferDataType format)
        {
            var span = data.AsSpan(byteOffset, byteLength);
            switch (format)
            {
                case BufferDataType.buffer_u8:
                    return [.. span.ToArray().Select(b => (b / 255f * 2) - 1)];
                case BufferDataType.buffer_s8:
                    return [.. MemoryMarshal.Cast<byte, sbyte>(span).ToArray().Select(b => b / 128f)];
                case BufferDataType.buffer_u16:
                    return [.. MemoryMarshal.Cast<byte, ushort>(span).ToArray().Select(v => (v / 65535f * 2) - 1)];
                case BufferDataType.buffer_s16:
                    return [.. MemoryMarshal.Cast<byte, short>(span).ToArray().Select(v => v / 32768f)];
                case BufferDataType.buffer_f32:
                    return MemoryMarshal.Cast<byte, float>(span).ToArray();
                default:
                    throw new NotImplementedException($"audio_create_buffer_sound doesn't support buffer format {format}");
            }
        }

        [GMLFunction("audio_create_buffer_sound")]
        public static object? audio_create_buffer_sound(object?[] args)
        {
            var bufferIndex = args[0].Conv<int>();
            var format = (BufferDataType)args[1].Conv<int>();
            var rate = args[2].Conv<int>();
            var offset = args[3].Conv<int>();
            var length = args[4].Conv<int>();
            var channels = args[5].Conv<int>();

            var buffer = BufferManager.Buffers[bufferIndex];
            var data = BufferBytesToFloatSamples(buffer.Data, offset, length, format);

            var alBuffer = AL.GenBuffer();
            AudioManager.CheckALError();
            AL.BufferData(alBuffer, channels == 2 ? ALFormat.StereoFloat32Ext : ALFormat.MonoFloat32Ext, data, rate);
            AudioManager.CheckALError();

            return AudioManager.RegisterAudioClip(new AudioAsset
            {
                Name = $"buffer_sound_{bufferIndex}",
                Clip = alBuffer,
                Gain = 1,
                Pitch = 1,
                Offset = 0,
            });
        }

        [GMLFunction("audio_free_buffer_sound")]
        public static object? audio_free_buffer_sound(object?[] args)
        {
            var index = args[0].Conv<int>();
            AudioManager.UnregisterAudio(index);
            return null;
        }

        // -- audio_*_play_queue -------------------------------------------------------------
        //
        // A queue of sound indices played back-to-back, one at a time -- AudioManager.Update()
        // (already polled once per frame to reclaim finished sources) additionally checks each
        // live queue's current instance and starts the next queued sound once it stops.

        [GMLFunction("audio_create_play_queue")]
        public static object? audio_create_play_queue(object?[] args)
        {
            var bus = args.Length > 0 ? args[0].Conv<int>() : 0;
            return AudioManager.CreatePlayQueue(bus);
        }

        [GMLFunction("audio_free_play_queue")]
        public static object? audio_free_play_queue(object?[] args)
        {
            var queue = args[0].Conv<int>();
            AudioManager.FreePlayQueue(queue);
            return null;
        }

        [GMLFunction("audio_queue_sound")]
        public static object? audio_queue_sound(object?[] args)
        {
            var queue = args[0].Conv<int>();
            var sound = args[1].Conv<int>();
            var offset = args.Length > 2 ? args[2].Conv<double>() : 0;
            return AudioManager.QueueSound(queue, sound, offset);
        }

        public static Dictionary<string, RecordingInfo> Recorders = new();

        [GMLFunction("audio_start_recording")]
        public static object? audio_start_recording(object?[] args)
        {
            var recorder_index = args[0].Conv<int>();
            var name = AudioManager.GetRecordingDeviceNames().ElementAt(recorder_index);

            ALCaptureDevice captureDevice;
            if (Recorders.TryGetValue(name, out var info))
            {
                if (info.Recording)
                {
                    throw new NotImplementedException("Already recording");
                }

                captureDevice = info.Device;
            }
            else
            {
                captureDevice = ALC.CaptureOpenDevice(name, 16000, ALFormat.Mono16, 32000);
                AudioManager.CheckALCError();

                info = new()
                {
                    SampleRate = 1600,
                    Format = ALFormat.Mono16,
                    Name = name,
                    Device = captureDevice
                };

                Recorders.Add(name, info);
            }

            ALC.CaptureStart(captureDevice);
            AudioManager.CheckALCError();

            info.Recording = true;

            return Recorders.Values.ToList().IndexOf(info);
        }

        [GMLFunction("audio_stop_recording")]
        public static object? audio_stop_recording(object?[] args)
        {
            var index = args[0].Conv<int>();
            var recorders = Recorders.Values.ToList();
            if (index < 0 || index >= recorders.Count)
            {
                return null;
            }

            var info = recorders[index];
            if (info.Recording)
            {
                ALC.CaptureStop(info.Device);
                AudioManager.CheckALCError();
                info.Recording = false;
            }
            return null;
        }

        [GMLFunction("audio_get_recorder_count")]
        public static object? audio_get_recorder_count(object?[] args)
        {
            return AudioManager.GetRecordingDeviceNames().Count();
        }

        [GMLFunction("audio_get_recorder_info")]
        public static object? audio_get_recorder_info(object?[] args)
        {
            var recorder_index = args[0].Conv<int>();
            var name = AudioManager.GetRecordingDeviceNames().ElementAt(recorder_index);

            var dsmap = DataStructuresFunctions.ds_map_create();
            DataStructuresFunctions.ds_map_add([dsmap, "name", name]);
            DataStructuresFunctions.ds_map_add([dsmap, "index", recorder_index]);
            DataStructuresFunctions.ds_map_add([dsmap, "data_format", BufferDataType.buffer_s16]);
            DataStructuresFunctions.ds_map_add([dsmap, "sample_rate", 16000]);
            DataStructuresFunctions.ds_map_add([dsmap, "channels", 0]); // TODO: not 1? or is audio_mono just an enum and that's the first value

            return dsmap;
        }

        // -- listener masks (GMS2's local-multiplayer/split-screen audio system) -----------------
        //
        // This engine only ever has one implicit listener (audio_get_listener_count is always 0 --
        // no separate listener-object tracking exists to route different sounds to different
        // listeners), so a "mask" has no real routing effect anywhere -- these are plain bookkeeping
        // stores/getters, matching whatever a game last set, so code that reads back its own mask
        // values doesn't see stale/wrong data even though nothing actually filters on them.

        private static readonly Dictionary<int, int> _soundListenerMasks = new();
        private static readonly Dictionary<int, int> _emitterListenerMasks = new();
        private static int _globalListenerMask = -1; // -1 = every listener, matches audio_lm_all's real value

        [GMLFunction("audio_sound_get_listener_mask")]
        public static object? audio_sound_get_listener_mask(object?[] args) =>
            _soundListenerMasks.GetValueOrDefault(args[0].Conv<int>(), -1);

        [GMLFunction("audio_sound_set_listener_mask")]
        public static object? audio_sound_set_listener_mask(object?[] args)
        {
            _soundListenerMasks[args[0].Conv<int>()] = args[1].Conv<int>();
            return null;
        }

        [GMLFunction("audio_emitter_get_listener_mask")]
        public static object? audio_emitter_get_listener_mask(object?[] args) =>
            _emitterListenerMasks.GetValueOrDefault(args[0].Conv<int>(), -1);

        [GMLFunction("audio_emitter_set_listener_mask")]
        public static object? audio_emitter_set_listener_mask(object?[] args)
        {
            _emitterListenerMasks[args[0].Conv<int>()] = args[1].Conv<int>();
            return null;
        }

        [GMLFunction("audio_get_listener_mask")]
        public static object? audio_get_listener_mask(object?[] args) => _globalListenerMask;

        [GMLFunction("audio_set_listener_mask")]
        public static object? audio_set_listener_mask(object?[] args)
        {
            _globalListenerMask = args[0].Conv<int>();
            return null;
        }

        [GMLFunction("audio_get_listener_info")]
        public static object? audio_get_listener_info(object?[] args)
        {
            var index = args[0].Conv<int>();

            var dsmap = DataStructuresFunctions.ds_map_create();
            DataStructuresFunctions.ds_map_add([dsmap, "index", index]);
            DataStructuresFunctions.ds_map_add([dsmap, "flags", _globalListenerMask]);
            DataStructuresFunctions.ds_map_add([dsmap, "x", AudioManager.Listener.Position.X]);
            DataStructuresFunctions.ds_map_add([dsmap, "y", AudioManager.Listener.Position.Y]);
            DataStructuresFunctions.ds_map_add([dsmap, "z", AudioManager.Listener.Position.Z]);
            return dsmap;
        }

        [GMLFunction("audio_get_listener_count", GMLFunctionFlags.Stub)]
        public static object? audio_get_listener_count(object?[] args)
        {
            return 0;
        }

        // -- audio_*_sync_group -------------------------------------------------------------
        //
        // Multiple tracks meant to stay locked together (e.g. layered music stems) -- staged via
        // audio_play_in_sync_group, then actually started back-to-back (in a tight loop, the
        // closest either this engine or real OpenAL's own source API can get to a hardware-exact
        // simultaneous start -- Web Audio's AudioBufferSourceNode.start(when) could schedule a
        // precise future time, but this engine's AL shim doesn't expose per-call scheduling, and
        // native has no such primitive at all) by audio_start_sync_group.

        private class SyncGroup
        {
            public bool Loop;
            public List<int> PendingSounds = new();
            public List<int> InstanceIds = new();
        }

        private static readonly Dictionary<int, SyncGroup> _syncGroups = new();
        private static int _nextSyncGroup = 1;

        [GMLFunction("audio_create_sync_group")]
        public static object? audio_create_sync_group(object?[] args)
        {
            var loop = args.Length > 0 && args[0].Conv<bool>();
            var id = _nextSyncGroup++;
            _syncGroups[id] = new SyncGroup { Loop = loop };
            return id;
        }

        [GMLFunction("audio_destroy_sync_group")]
        public static object? audio_destroy_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (_syncGroups.Remove(group, out var g))
            {
                foreach (var instanceId in g.InstanceIds)
                {
                    var instance = AudioManager.GetAudioInstance(instanceId);
                    if (instance != null)
                    {
                        AL.SourceStop(instance.Source);
                    }
                }
            }
            return null;
        }

        [GMLFunction("audio_play_in_sync_group")]
        public static object? audio_play_in_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            var sound = args[1].Conv<int>();
            if (_syncGroups.TryGetValue(group, out var g))
            {
                g.PendingSounds.Add(sound);
            }
            return null;
        }

        [GMLFunction("audio_start_sync_group")]
        public static object? audio_start_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (!_syncGroups.TryGetValue(group, out var g))
            {
                return null;
            }

            foreach (var sound in g.PendingSounds)
            {
                var asset = AudioManager.GetAudioAsset(sound);
                var instanceId = AudioManager.audio_play_sound(sound, 0, g.Loop, asset?.Gain ?? 1, asset?.Offset ?? 0, asset?.Pitch ?? 1);
                g.InstanceIds.Add(instanceId);
            }
            g.PendingSounds.Clear();
            return null;
        }

        [GMLFunction("audio_pause_sync_group")]
        public static object? audio_pause_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (_syncGroups.TryGetValue(group, out var g))
            {
                foreach (var instanceId in g.InstanceIds)
                {
                    var instance = AudioManager.GetAudioInstance(instanceId);
                    if (instance != null)
                        AL.SourcePause(instance.Source);
                }
            }
            return null;
        }

        [GMLFunction("audio_resume_sync_group")]
        public static object? audio_resume_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (_syncGroups.TryGetValue(group, out var g))
            {
                foreach (var instanceId in g.InstanceIds)
                {
                    var instance = AudioManager.GetAudioInstance(instanceId);
                    if (instance != null)
                        AL.SourcePlay(instance.Source);
                }
            }
            return null;
        }

        [GMLFunction("audio_stop_sync_group")]
        public static object? audio_stop_sync_group(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (_syncGroups.TryGetValue(group, out var g))
            {
                foreach (var instanceId in g.InstanceIds)
                {
                    var instance = AudioManager.GetAudioInstance(instanceId);
                    if (instance != null)
                        AL.SourceStop(instance.Source);
                }
                g.InstanceIds.Clear();
            }
            return null;
        }

        [GMLFunction("audio_sync_group_get_track_pos")]
        public static object? audio_sync_group_get_track_pos(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (_syncGroups.TryGetValue(group, out var g) && g.InstanceIds.Count > 0)
            {
                var instance = AudioManager.GetAudioInstance(g.InstanceIds[0]);
                if (instance != null)
                {
                    var offset = AL.GetSource(instance.Source, ALSourcef.SecOffset);
                    AudioManager.CheckALError();
                    return (double)offset;
                }
            }
            return 0d;
        }

        [GMLFunction("audio_sync_group_debug", GMLFunctionFlags.Stub)]
        public static object? audio_sync_group_debug(object?[] args) => null;

        [GMLFunction("audio_sync_group_is_playing")]
        public static object? audio_sync_group_is_playing(object?[] args)
        {
            var group = args[0].Conv<int>();
            if (!_syncGroups.TryGetValue(group, out var g))
            {
                return false;
            }

            return g.InstanceIds.Any(id => AudioManager.GetAudioInstance(id) != null);
        }

        [GMLFunction("audio_create_stream")]
        public static object? audio_create_stream(object?[] args)
        {
            var filename = args[0].Conv<string>();
            // Through the shared resolver rather than a bare Path.Combine: that is what turns a
            // game's Windows-style backslashes into a real path, and what finds a file whose name
            // differs only in case (this game asks for musKickIt.ogg and ships musKickit.ogg).
            filename = Entry.ResolveDataWinPath(filename);

            var assetName = Path.GetFileNameWithoutExtension(filename);
            var existingIndex = AssetIndexManager.GetIndex(AssetType.sounds, assetName);
            if (existingIndex != -1)
            {
                // happens in deltarune on battle.ogg
                DebugLog.LogWarning($"audio_create_stream on {filename} already registered with index {existingIndex}");
                return existingIndex;
            }

            // Registered, not decoded: GameMaker streams this off disk, and decoding it to PCM
            // here is what ran the browser build out of heap. See AudioManager.RegisterStreamedFile.
            return AudioManager.RegisterStreamedFile(assetName, filename);
        }

        [GMLFunction("audio_destroy_stream")]
        public static object? audio_destroy_stream(object?[] args)
        {
            var index = args[0].Conv<int>();
            AudioManager.UnregisterAudio(index);
            return null;
        }

        // Real GMS shows a live in-engine audio-graph overlay; this engine has no such overlay to
        // toggle, so this just flips log verbosity up/down as the closest available substitute for
        // "give me more insight into what the audio system is doing".
        [GMLFunction("audio_debug")]
        public static object? audio_debug(object?[] args)
        {
            var enable = args[0].Conv<bool>();
            DebugLog.Verbosity = enable ? DebugLog.LogType.Verbose : DebugLog.LogType.Info;
            return null;
        }
    }

    public class RecordingInfo
    {
        public int SampleRate;
        public ALFormat Format;
        public string Name = "";
        public bool Recording;
        public ALCaptureDevice Device;
    }
}
