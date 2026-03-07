using Anaglyph.Netcode;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Demo
{
    /// <summary>
    /// Fully automatic host/guest assignment:
    ///
    ///   1. App opens → MetaSessionDiscovery starts scanning for a nearby host
    ///   2a. Host found within timeout → auto-connect as guest (MetaSessionDiscovery handles this)
    ///   2b. No host found after timeout → automatically StartHost (become the host)
    ///
    /// "Whoever opens the app first becomes the host; everyone else auto-joins."
    /// A random jitter (4–7 s) prevents two devices timing out simultaneously.
    /// </summary>
    public class DemoNetworkUI : MonoBehaviour
    {
        [SerializeField] private Text statusText;

        // How long to wait for a host before self-hosting.
        // Random jitter reduces the chance of two devices timing out at the same moment.
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
                return; // state changed (connected/connecting) before timeout — do nothing
            }

            // Still disconnected after waiting → no host nearby → become the host
            if (NetcodeManagement.State == NetcodeState.Disconnected)
                NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
        }

        private void OnStateChanged(NetcodeState state)
        {
            // Cancel the auto-host timer the moment we start connecting
            if (state != NetcodeState.Disconnected)
                cts?.Cancel();

            switch (state)
            {
                case NetcodeState.Disconnected:
                    statusText.text = "Looking for host...";
                    gameObject.SetActive(true);
                    break;
                case NetcodeState.Connecting:
                    statusText.text = "Connecting...";
                    break;
                case NetcodeState.Connected:
                    gameObject.SetActive(false); // hide UI entirely once in session
                    break;
            }
        }
    }
}
