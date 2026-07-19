using UnityEngine;

/// <summary>
/// 玩家/世界/层 的唯一换算点。
/// 约定：playerId（= NGO clientId）== worldId ==  Unity Layer "layer{N}"。
/// "这东西是谁的 / 在哪个世界 / 该在哪个层"全部经由这里换算，
/// 业务代码里不要再手写 `== 0 ? "layer0" : "layer1"` 或 `(ulong)world` 这类硬转。
/// </summary>
public static class PlayerWorld
{
    /// <summary>当前 demo 是双人双世界。扩人数时先改这里，再全局搜 Other* 的调用点逐个确认语义。</summary>
    public const int WorldCount = 2;

    // ── playerId ↔ worldId ──────────────────────────────────────

    public static int WorldOf(ulong playerId) => (int)playerId;
    public static ulong PlayerOf(int worldId) => (ulong)worldId;

    // ── worldId → Unity Layer ───────────────────────────────────

    public static string LayerNameOf(int worldId) => "layer" + worldId;
    public static string LayerNameOfPlayer(ulong playerId) => LayerNameOf(WorldOf(playerId));

    // ── 双人特例（>2 人时"对面"不再唯一，这些方法会失效，见 WorldCount）──

    public static int OtherWorld(int worldId) => 1 - worldId;
    public static ulong OtherPlayer(ulong playerId) => PlayerOf(OtherWorld(WorldOf(playerId)));

    /// <summary>把物体（含全部子物体）放到其属主玩家的世界层上。</summary>
    public static void ApplyOwnerLayer(GameObject go, ulong ownerPlayerId)
    {
        ChangeLayer.Instance?.ChangeObjectLayer(go, LayerMask.GetMask(LayerNameOfPlayer(ownerPlayerId)));
    }
}
