import { GameError } from './game.mjs';

// Command identifiers observed in the APK's Lua login/attribute descriptors.
// This bridge carries decoded messages over local HTTP, not MOS2/NEX wire bytes.
export const clientCommands = Object.freeze({ auth: 258, roles: 263, enter: 260, heartbeat: 1020, device: 1001 });

export function clientMessage(game, authorization, input, log = () => {}) {
  if (!input || !Number.isInteger(input.command) || !input.payload || typeof input.payload !== 'object' || Array.isArray(input.payload)) {
    throw new GameError(400, 'invalid_input', 'command and payload object required');
  }
  const command = input.command;
  if (command === clientCommands.auth) {
    if (input.payload.account_name !== 'local-apk-guest' || input.payload.sdkuid !== 'local-apk-guest') {
      throw new GameError(400, 'local_guest_required', 'Only the local APK guest is supported');
    }
    const session = game.createSession({ name: '本地测试旅人' });
    log({ event: 'client.auth', command, transport: 'local-http' });
    return { token: session.token, messages: [{ command: 364, payload: {
      status: 0, aid: 1001, server_id: 1, account_name: 'local-apk-guest',
      auth_sn: 'local-session', role_name: session.player.name,
      server_time: Math.floor(game.now() / 1000), front: 0, rank: 0,
      enter_limit: 1, is_pto_diff: 0,
    } }] };
  }
  const playerId = game.authenticate(authorization);
  const player = game.snapshot(playerId);
  if (command === clientCommands.roles) {
    log({ event: 'client.roles', command });
    return { messages: [{ command: 358, payload: { status: 0, role_list: [
      { server_id: 1, role_id: 1001, role_name: player.name, name_inited: 1 },
    ] } }] };
  }
  if (command === clientCommands.enter) {
    if (input.payload.role_id !== 1001 || input.payload.server_id !== 1) {
      throw new GameError(400, 'unknown_role', 'Unknown local role');
    }
    log({ event: 'client.enter', command });
    return { messages: [{ command: 360, payload: { status: 0, connect_id: 'local-http-session' } }] };
  }
  if (command === clientCommands.heartbeat) {
    log({ event: 'client.heartbeat', command });
    return { messages: [{ command: 893, payload: { status: 0, timestamp: Math.floor(game.now() / 1000) } }] };
  }
  if (command === clientCommands.device) {
    log({ event: 'client.device', command });
    return { messages: [] };
  }
  log({ event: 'client.unsupported', command });
  throw new GameError(501, 'client_command_not_implemented', `Local client command ${command} is not implemented`);
}
