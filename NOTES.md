
# 2026-04-16

Here is my proposed plan to improve the robustness of the Relay Server (Item 6):

  1. Lagging Client Detection (Stall Prevention)
  Problem: In lockstep networking, if one client lags severely or stops sending data, the entire simulation
  stalls for everyone else waiting for their input.
  Solution:
   * Add a HighestSeenTick property to the Connection class to track how far each individual client has
     progressed.
   * Update the StartReaper background task (which currently only cleans up idle rooms every minute) to run
     more frequently (e.g., every 5 seconds).
   * During this check, if any client in a Playing room falls too far behind the room's overall HighestSeenTick
     (e.g., > 60 ticks, which is 5 seconds of simulation time), forcibly close their WebSocket. The existing
     disconnect logic will then broadcast a PlayerLeft message, allowing the remaining players to continue the
     game without stalling.

  2. Dead Connection Detection
  Problem: If a client's internet connection drops silently (half-open TCP connection), the server might not
  realize they are gone for 15+ minutes, stalling the room the entire time.
  Solution:
   * Leverage the existing LastMessageAt property on the Connection object.
   * In the frequent StartReaper check, automatically disconnect any client that hasn't sent a message in the
     last 10–15 seconds. This guarantees dead connections are dropped quickly, freeing up the lockstep
     simulation.

  3. Hardened Packet Parsing
  Problem: While some message handlers use try-catch to swallow errors, Varint parsing and other byte
  manipulation might still cause IndexOutOfRangeException if a client sends maliciously small or malformed
  packets.
  Solution:
   * Audit the parsing logic in RelayServer.cs (specifically HandleCreateRoom, HandleJoinRoom, and
     HandleUpdateRoom) to ensure strict boundary checks are performed before accessing the payload spans.
   * Explicitly disconnect clients that send malformed payloads (protocol violation) rather than just dropping
     the packet, as this usually indicates a tampered client or severe desync.
