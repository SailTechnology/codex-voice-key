using System;
using System.Collections.Generic;
using System.IO;

namespace CodexVoiceKey
{
    internal static class AudioMuteTests
    {
        private sealed class FakeOutputs : IAudioOutputs
        {
            internal readonly Dictionary<string, bool> States = new Dictionary<string, bool>();
            internal string Offline, FailMute;
            internal Dictionary<string, bool> Journal = new Dictionary<string, bool>();
            internal bool FailSave;
            internal int Changes;
            public Dictionary<string, bool> SnapshotActive()
            {
                Dictionary<string, bool> active = new Dictionary<string, bool>(States);
                if (Offline != null) active.Remove(Offline);
                return active;
            }
            public void SetMute(string id, bool muted)
            {
                if (id == Offline || (id == FailMute && muted)) throw new IOException("Device unavailable.");
                if (muted && !Journal.ContainsKey(id)) throw new Exception("Muted an output before saving its original state.");
                States[id] = muted; Changes++;
            }
            internal void Save(Dictionary<string, bool> states)
            {
                if (FailSave) throw new IOException("Cannot persist audio state.");
                Journal = new Dictionary<string, bool>(states);
            }
            internal AudioMuteLease Lease()
            { return new AudioMuteLease(this, Journal, Save, delegate { }); }
        }

        internal static void Run(Action<bool, string> check)
        {
            FakeOutputs audio = new FakeOutputs();
            audio.States["speakers"] = false; audio.States["already-muted"] = true;
            AudioMuteLease lease = audio.Lease();
            lease.MuteActive();
            check(audio.States["speakers"] && audio.States["already-muted"], "Recording silences all active output endpoints");
            check(!audio.Journal["speakers"] && audio.Journal["already-muted"], "Original mute states are persisted before changing outputs");
            lease.MuteActive();
            check(!audio.Journal["speakers"], "Repeated mute checks never replace the original unmuted state");
            audio.States["already-muted"] = false;
            lease.MuteActive();
            check(audio.States["already-muted"], "Playback stays silent throughout a recording");
            audio.States["new-headphones"] = false;
            lease.MuteActive();
            check(audio.States["new-headphones"] && !audio.Journal["new-headphones"], "Newly connected outputs join the same restoration lease");
            lease.Restore();
            check(!audio.States["speakers"] && !audio.States["new-headphones"], "Previously audible outputs resume after recording");
            check(audio.States["already-muted"], "Originally muted outputs are never force-unmuted");
            check(lease.PendingCount == 0 && audio.Journal.Count == 0, "Successful restoration clears the recovery journal");
            int changes = audio.Changes;
            lease.Restore();
            check(audio.Changes == changes, "Repeated cleanup cannot toggle audio a second time");

            audio = new FakeOutputs(); audio.States["speaker"] = false; audio.FailSave = true;
            bool failed = false;
            try { audio.Lease().MuteActive(); } catch (IOException) { failed = true; }
            check(failed && !audio.States["speaker"] && audio.Changes == 0, "Persistence failure leaves the original sound state intact");

            audio = new FakeOutputs(); audio.States["speaker"] = false; audio.States["usb"] = false;
            audio.FailMute = "usb"; lease = audio.Lease(); failed = false;
            try { lease.MuteActive(); } catch (IOException) { failed = true; }
            finally { lease.Restore(); }
            check(failed && !audio.States["speaker"] && !audio.States["usb"], "Partial initialization failure restores every affected output");

            audio = new FakeOutputs(); audio.States["speaker"] = false; audio.States["usb"] = false;
            lease = audio.Lease(); lease.MuteActive(); audio.Offline = "usb"; lease.Restore();
            check(!audio.States["speaker"] && lease.PendingCount == 1 && !audio.Journal["usb"], "Disconnected outputs remain in the recovery journal without blocking other restoration");
            audio.Offline = null;
            AudioMuteLease recovery = audio.Lease(); recovery.Restore();
            check(!audio.States["usb"] && audio.Journal.Count == 0, "A later process can restore a previously disconnected output");

            audio = new FakeOutputs(); audio.States["speaker"] = false;
            lease = audio.Lease(); lease.MuteActive();
            recovery = audio.Lease(); recovery.Restore();
            check(!audio.States["speaker"] && audio.Journal.Count == 0, "Persisted state survives loss of the original mute owner");
            audio = new FakeOutputs(); lease = audio.Lease(); lease.MuteActive(); lease.Restore();
            check(audio.Changes == 0 && lease.PendingCount == 0, "Computers without active outputs can still record");
        }
    }
}
