using OverlayPoc.Services;

namespace OverlayPoc;

internal sealed partial class OverlayForm
{
    private async Task ToggleSpeechInputAsync()
    {
        if (_speechStarting || _speechStopping)
            return;

        if (_speechInput is not null)
        {
            await FinishSpeechInputAsync();
            return;
        }

        _speechStarting = true;
        _speechPrefix = _input.Text.TrimEnd();
        SetSpeechUiState("Preparing microphone...");

        var speechInput = new SpeechInputService();
        speechInput.TranscriptUpdated += UpdateSpeechTranscript;
        try
        {
            await speechInput.StartAsync(SetSpeechUiState);
            _speechInput = speechInput;
            SetSpeechUiState("Listening · Ctrl+Shift+L to stop and send");
        }
        catch (Exception ex)
        {
            speechInput.TranscriptUpdated -= UpdateSpeechTranscript;
            await speechInput.DisposeAsync();
            _speechStarting = false;
            SetSpeechUiState("Microphone unavailable");
            Program.Log("Start speech input failed: " + ex);
            MessageBox.Show(this, ex.Message, "Could not start listening",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateInteractionVisuals();
            return;
        }

        _speechStarting = false;
    }

    private async Task FinishSpeechInputAsync()
    {
        _speechStopping = true;
        SetSpeechUiState("Transcribing final audio...");

        var speechInput = _speechInput;
        _speechInput = null;
        string transcript;
        try
        {
            transcript = await speechInput.StopAsync();
        }
        catch (Exception ex)
        {
            Program.Log("Stop speech input failed: " + ex);
            MessageBox.Show(this, ex.Message, "Speech transcription failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            transcript = "";
        }
        finally
        {
            speechInput.TranscriptUpdated -= UpdateSpeechTranscript;
            await speechInput.DisposeAsync();
            _speechStopping = false;
        }

        if (!string.IsNullOrWhiteSpace(transcript))
        {
            SetInputTextWithSpeech(transcript);
            SendMessage();
            UpdateInteractionVisuals();
        }
        else
        {
            SetSpeechUiState("No speech detected");
        }
    }

    private async Task DisposeSpeechInputOnCloseAsync(SpeechInputService speechInput)
    {
        speechInput.TranscriptUpdated -= UpdateSpeechTranscript;
        try
        {
            await speechInput.DisposeAsync();
        }
        catch (Exception ex)
        {
            Program.Log("Dispose speech input failed: " + ex);
        }
    }

    private void UpdateSpeechTranscript(string transcript)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(new Action(() => SetInputTextWithSpeech(transcript)));
    }

    private void SetInputTextWithSpeech(string transcript)
    {
        var separator = string.IsNullOrEmpty(_speechPrefix) ? "" : " ";
        _input.Text = _speechPrefix + separator + transcript;
        _input.SelectionStart = _input.Text.Length;
        _hud?.SetTypingText(_input.Text);
    }

    private void SetSpeechUiState(string status)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(new Action(() =>
        {
            if (_titleLabel is not null)
                _titleLabel.Text = status;
            if (_listenMenuItem is not null)
                _listenMenuItem.Text = _speechInput is not null || _speechStarting || _speechStopping
                    ? "Stop listening and send (Ctrl+Shift+L)"
                    : "Start listening (Ctrl+Shift+L)";
        }));
    }
}
