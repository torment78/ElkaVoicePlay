# Interactive acceptance

Live microphone quality, physical button learning and game compatibility require a person at the computer. Automated and local input tests supplement these checks.

## Completed transcription, translation and speakers (0.4.0)

1. Switch between Transcribe audio and Game profiles. Confirm it stays the same window, retains input choices/preview and does not add a second taskbar window. Game talk/command buttons must remain inactive during transcription, including while finishing before a switch.
2. Select and name a real microphone, the output playing your test video, and a separate VoiceMeeter route. Check the corresponding meters while recording known speech on each route, including simultaneous speech and silent intervals.
3. Speak continuously and with pauses. Expect completed lines after a pause; uninterrupted audio can take up to 30 seconds plus processing. Lines must appear only once and must not change or reorder. Record more than 30 seconds, then Stop & save. In combined mode, verify input labels and chronological timestamps against the known material. No silent intervals should compress the shared timeline.
4. Untick one input and record in separate mode. Expect files only for the enabled inputs and no transcription from the unticked route. Restart and confirm the settings saved at Start return.
5. Stop during speech and return to Game profiles during a recording. Both must finish buffered audio before closing. Reopen saved files and confirm the final lines remain.
6. Disconnect an enabled device and try a deliberately heavy model/three-input load. Expect a visible error on lost samples or backlog and readable completed text, not an unlimited queue or silent loss.
7. Enable Label speakers and record known alternating voices, including one returning after a pause and voices talking together. Compare labels with the actual people. Numbering is separate per input/session; this feature remains experimental.
8. Choose a known non-English input language and English output, then compare the result with the spoken meaning. Repeat with Same as input. Automated fixtures do not establish accuracy on your own recordings.
9. Run the affected VoiceMeeter loopback for several minutes with video speech, silence and resume. Confirm the previous 1–4 ms timing error does not recur. Check settings are retained after Start and restart.

## Complete-folder first launch (0.2.3)

1. Send the complete application ZIP to a tester using another Windows account/PC. Extract all files, including Models/ggml-base.bin, before launching the EXE.
2. With no previous VoicePlay model installed, start the app. Expect preparation followed by Ready, with Base installed under that user's actual Documents/ElkaSoftware/ElkaVoicePlay/Models directory.
3. Restart and confirm the installed model is reused. Choose a microphone and test speech; do not copy the sender's settings file.
4. Check a redirected Documents location when available. Confirm existing custom model choices and existing Base files are preserved.

## Chat-switch command (0.2.2)

1. Edit a profile, expand Chat-switch command and enable it. Start with Enter / Tab / Enter and suitable delays for that app. Save and restart; verify all choices persist.
2. Run Test chat sequence locally → Test command buttons. Expect the three configured down/up pairs in order, with no text inserted.
3. With double-tap mode selected, physically tap and release the talk button twice within the configured window. Expect one command, no microphone recording and no change of selected profile. A single short tap, slow taps and keyboard repeat must not run the command.
4. Hold the talk button beyond the window, then speak. Confirm one recording and ordinary delivery on release. Test a short tap followed by a long hold, and change the window to suit your tapping speed.
5. Tick Use a separate button, choose or Learn a distinct key/mouse button and save. One complete press should run one command; holding it must not repeat the command. Normal hold-to-talk should start immediately.
6. In the intended game/app, begin with chat empty and closed. Confirm the example sequence rotates the chat destination once. Check every supported destination; this is app-dependent.
7. Switch focus during a deliberately long command delay, disable VoicePlay or press Cancel/Escape. Later steps must stop. Check chat before retrying; no automatic retry should occur.
8. With a linked profile, focus another app and activate the command. Expect a warning and no output. An automatic profile must use its own command, button/mode and timing. Disable the command and confirm ordinary hold-to-talk behavior returns.

## Idle performance and delivery timing

1. With VoicePlay closed, turn the camera continuously in the affected game. Start 0.2.1 from the updated desktop folder, wait for Ready, then repeat without pressing the talk button. Confirm whether the periodic stutter is gone. Repeat with VoicePlay minimized to the tray.
2. Hold the talk button outside the linked game, speak and release. If the destination is still invalid when transcription finishes, expect a warning, retained recognized text and no inserted characters or chat actions.
3. Start a recording outside the linked game, then focus that game before transcription finishes. The destination is selected when the text is ready, and the normal sequence should run.
4. While idle, the microphone level meter should stay still. It should update only during a recording when the main window is visible.

## Profiles and game chat

1. Choose War Dogs and Edit / Learn. Click Learn for the talk button, press and release a physical keyboard key or mouse side button, then Save. Confirm the displayed binding matches. Repeat learning with another button and restore your preference.
2. Confirm holding starts one recording, repeats do not restart it, and release starts transcription. Test suppression enabled and disabled. The game chat must not open until valid transcription is ready.
3. Run Test chat sequence locally. Expect the exact sample, one submission, two balanced Enter down/up pairs and closed chat with the War Dogs preset.
4. In War Dogs, use Enter to open, Enter to send, and no third close action. Try a short message, confirm exact delivery and return to gameplay. Adjust the opening wait and character delay only if needed.
5. For a game that needs a separate close key, configure it and verify that it runs after submission. Disable automatic sending and confirm text stays open for manual review, with no close action.
6. Create another profile with a different trigger and sequence. Verify both survive an app restart and manual selection works with automatic selection disabled.
7. Link a profile to its actual game EXE and enable automatic selection for it. Its talk button becomes available alongside the selected manual profile's button. Focus that game before transcription finishes and verify the correct sequence is chosen at delivery. Repeat with a different linked game. A game running only in the background must not receive input.
8. Leave the EXE link blank and automatic activation off. Confirm the manual profile works in the focused intended app. Linked manual profiles should reject other executables.
9. Switch away during recording/recognition and back before text is ready: delivery should still work. Switch away during the opening delay or typing: later typing/send/close steps must stop. Inspect any partially filled chat before retrying.
10. Cancel during recording, recognition and a long opening delay. Confirm recovery on the next press. Already delivered text must never be retried automatically.
11. Hold the trigger before enabling VoicePlay, then enable it while the button remains down. No recording should start from repeats; release and press again. Starting outside the target after VoicePlay is enabled is allowed, with destination validation deferred until delivery.

Current status: production input path passed the local Enter/text/Enter receiver with exact Unicode text in 0.2.0. Physical Learn, physical talk-button capture, idle game performance and actual game delivery still need user confirmation on 0.2.1. The War Dogs preset has no EXE link until the user selects the actual game executable; existing user links and delays are preserved by the update.

## Recognition and Notepad

1. Start VoicePlay and confirm Base reaches Ready. Choose the intended microphone/language and Apply settings.
2. Hold Hold to test transcription, speak, release. Confirm the input level and recognized text. Nothing should be inserted elsewhere.
3. Select Notepad, focus a blank Notepad document, hold F9 (or your chosen button), speak and release. Expect one copy and no Enter. Test Notepad input isolates native insertion without microphone use.
4. Try several phrases, including Icelandic if needed. Judge real accuracy and latency. Select the intended tab before transcription finishes; switching away during insertion cancels delivery.
5. Confirm Esc cancellation, recording/message limits, microphone disconnect/reconnect recovery, tray enable/disable and clean Exit.

Earlier user result: speech recognition was correct. Notepad's original repeated/dropped character path was corrected in 0.1.1 and visually verified with a fixed sample; full physical speech-to-Notepad recheck remains pending. The user subsequently explicitly requested the game/profile functionality implemented in 0.2.0.

## 0.2.4 keyboard protection

- Enable **Block keyboard input while sending** for a game profile and save; reopen the editor to confirm persistence.
- Run **Test chat sequence locally**: check the On indicator, exact text and one send. During delivery, try typing ordinary letters; they must not enter the message. Repeat with the option off to compare.
- In the actual game, try holding W before delivery, then releasing during typing. It must not add repeated W characters or remain stuck after release. Try a new key press during delivery; it must be discarded. Confirm keyboard control returns afterward.
- Press physical Escape during a longer sequence: later steps stop and normal keyboard use returns. Mouse remains available throughout. Check partial text manually; no automatic retry.
- Switch focus with the mouse during delivery or cancel through the UI; protection must end and later sends must stop. For manual-send profiles, keyboard control must return after typing so Enter can be pressed manually.
- A delivery over 15 seconds must stop with a timeout and restore input. Ordinary idle gameplay should remain unaffected.

## 0.5.0 transcription model comparison

On Transcribe audio choose Large-v3, enable Label speakers, select Nemotron-3 and Automatic processing. Start the same video used with the prior build. Check the displayed processing backend, completed-line timing, anonymous speaker consistency and Stop & save. Compare Classic versus Nemotron using a new session for each. Test brief replies, laughter, overlapping dialogue and returning voices. Confirm the game tab still uses the original model/profile and does not load transcription GPU workers while idle. Automated fixtures establish integration only; this real-video comparison remains user acceptance.
