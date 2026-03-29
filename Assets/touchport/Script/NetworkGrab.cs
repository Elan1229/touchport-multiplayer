using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkGrab : NetworkBehaviour
{
    private Transform heldBy;
    private bool isLocallyGrabbed;

    // Called by CubeGrabber when a hand closes near the cube
    public void OnGrabbed(Transform handTransform)
    {
        isLocallyGrabbed = true;
        heldBy = handTransform;
        RequestOwnershipServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    // Called by CubeGrabber when the hand opens
    public void OnReleased()
    {
        isLocallyGrabbed = false;
        heldBy = null;
    }

    // Non-owners ask the server to transfer ownership to them
    [ServerRpc(RequireOwnership = false)]
    private void RequestOwnershipServerRpc(ulong clientId)
    {
        NetworkObject.ChangeOwnership(clientId);
    }

    private void Update()
    {
        // Only the owner moves the cube; NetworkTransform syncs it to everyone else
        if (IsOwner && isLocallyGrabbed && heldBy != null)
        {
            transform.position = heldBy.position;
            transform.rotation = heldBy.rotation;
        }
    }
}
