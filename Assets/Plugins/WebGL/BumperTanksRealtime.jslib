mergeInto(LibraryManager.library, {
  $BumperTanksSockets: {},

  BumperSocketConnect__deps: ['$BumperTanksSockets', '$UTF8ToString'],
  BumperSocketConnect: function (urlPtr, id, receiverPtr) {
    var key = id.toString();
    var url = UTF8ToString(urlPtr);
    var receiver = UTF8ToString(receiverPtr);
    try {
      var socket = new WebSocket(url);
      socket.binaryType = "arraybuffer";
      var state = { socket: socket, receiver: receiver, connectTimeout: 0 };
      BumperTanksSockets[key] = state;
      state.connectTimeout = setTimeout(function () {
        if (BumperTanksSockets[key] && socket.readyState === WebSocket.CONNECTING) {
          SendMessage(receiver, "OnWebSocketError", key + "\nRealtime connection timed out.");
          socket.close();
        }
      }, 15000);
      socket.onopen = function () {
        clearTimeout(state.connectTimeout);
        if (BumperTanksSockets[key]) SendMessage(receiver, "OnWebSocketOpen", key);
      };
      socket.onmessage = function (event) {
        if (!BumperTanksSockets[key]) return;
        if (typeof event.data !== "string") {
          SendMessage(receiver, "OnWebSocketError", key + "\nBinary realtime frames are unsupported.");
          return;
        }
        if (new TextEncoder().encode(event.data).length > 16384) {
          SendMessage(receiver, "OnWebSocketError", key + "\nRealtime message exceeded the 16 KB limit.");
          socket.close(1009, "Message too large");
          return;
        }
        SendMessage(receiver, "OnWebSocketMessage", key + "\n" + event.data);
      };
      socket.onerror = function () {
        if (BumperTanksSockets[key]) SendMessage(receiver, "OnWebSocketError", key + "\nRealtime WebSocket error.");
      };
      socket.onclose = function (event) {
        if (!BumperTanksSockets[key]) return;
        clearTimeout(state.connectTimeout);
        delete BumperTanksSockets[key];
        SendMessage(receiver, "OnWebSocketClose", key + "\n" + (event.reason || "Connection closed."));
      };
    } catch (error) {
      SendMessage(receiver, "OnWebSocketError", key + "\n" + (error && error.message ? error.message : "Unable to open WebSocket."));
      SendMessage(receiver, "OnWebSocketClose", key);
    }
  },

  BumperSocketSend__deps: ['$BumperTanksSockets', '$UTF8ToString'],
  BumperSocketSend: function (id, messagePtr) {
    var entry = BumperTanksSockets[id.toString()];
    if (!entry || entry.socket.readyState !== WebSocket.OPEN) return;
    var message = UTF8ToString(messagePtr);
    if (new TextEncoder().encode(message).length > 16384) {
      SendMessage(entry.receiver, "OnWebSocketError", id.toString() + "\nRealtime message exceeded the 16 KB limit.");
      return;
    }
    try { entry.socket.send(message); }
    catch (error) {
      SendMessage(entry.receiver, "OnWebSocketError", id.toString() + "\n" + (error && error.message ? error.message : "WebSocket send failed."));
    }
  },

  BumperSocketClose__deps: ['$BumperTanksSockets'],
  BumperSocketClose: function (id) {
    var key = id.toString();
    var entry = BumperTanksSockets[key];
    if (!entry) return;
    try { entry.socket.close(1000, "Client disconnected."); }
    catch (error) { delete BumperTanksSockets[key]; }
  }
});
