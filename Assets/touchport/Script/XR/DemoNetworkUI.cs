using Anaglyph.Netcode;
using System.Threading;
using UnityEngine;
using TMPro;

public class DemoNetworkUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;

    private const float MinWaitSeconds = 4f;
    private const float MaxWaitSeconds = 7f;

    private CancellationTokenSource cts;

    private void Start()
    {
        NetcodeManagement.StateChanged += OnStateChanged;
        OnStateChanged(NetcodeManagement.State);
        BeginAutoHost();
    }

    private void OnDestroy()
    {
        cts?.Cancel();
        NetcodeManagement.StateChanged -= OnStateChanged;
    }

    private async void BeginAutoHost()
    {
        cts = new CancellationTokenSource();

        float wait = Random.Range(MinWaitSeconds, MaxWaitSeconds);
        try
        {
            await Awaitable.WaitForSecondsAsync(wait, cts.Token);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }

        if (NetcodeManagement.State == NetcodeState.Disconnected)
            NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
    }

    private void OnStateChanged(NetcodeState state)
    {
        if (state != NetcodeState.Disconnected)
            cts?.Cancel();

        switch (state)
        {
            case NetcodeState.Disconnected:
                if (statusText != null) statusText.text = "Looking for host...";
                gameObject.SetActive(true);
                break;
            case NetcodeState.Connecting:
                if (statusText != null) statusText.text = "Connecting...";
                break;
            case NetcodeState.Connected:
                gameObject.SetActive(false);
                break;
        }
    }
}
