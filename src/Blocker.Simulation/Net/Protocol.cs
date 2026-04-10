// src/Blocker.Simulation/Net/Protocol.cs
namespace Blocker.Simulation.Net;

/// <summary>
/// Wire protocol constants. See docs/superpowers/specs/2026-04-10-multiplayer-design.md §"Wire protocol".
/// Ranges: 0x00–0x0F session, 0x10–0x1F tick hot path, 0x20–0x2F diagnostics,
/// 0x30–0x3F social (reserved), 0x40–0x4F bulk (reserved).
/// </summary>
public static class Protocol
{
    public const byte ProtocolVersion = 1;
    public const ushort SimulationVersion = 1;

    // Session / lobby — 0x00–0x0F
    public const byte Hello        = 0x01;
    public const byte HelloAck     = 0x02;
    public const byte CreateRoom   = 0x03;
    public const byte JoinRoom     = 0x04;
    public const byte RoomState    = 0x05;
    public const byte LeaveRoom    = 0x06;
    public const byte StartGame    = 0x07;
    public const byte GameStarted  = 0x08;

    // Tick traffic — 0x10–0x1F
    public const byte Commands     = 0x10;
    public const byte Hash         = 0x11;
    public const byte PlayerLeft   = 0x12;
    public const byte Surrender    = 0x13;

    // Diagnostics — 0x20–0x2F
    public const byte DesyncReport = 0x20;
    public const byte Error        = 0x21;
    public const byte Ping         = 0x22;
    public const byte Pong         = 0x23;
}

public enum LeaveReason : byte
{
    Disconnected = 0,
    Surrender    = 1,
    Kicked       = 2,
}

public enum ErrorCode : byte
{
    Unknown             = 0,
    ProtocolMismatch    = 1,
    RoomNotFound        = 2,
    RoomFull            = 3,
    SlotTaken           = 4,
    RateLimit           = 5,
    MessageTooLarge     = 6,
    TooManyRooms        = 7,
    UnknownMessageType  = 8,
    NotInRoom           = 9,
    NotHost             = 10,
}
