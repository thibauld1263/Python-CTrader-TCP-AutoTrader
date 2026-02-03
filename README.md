# Python-CTrader-AutoTrader

This cTrader cBot streams market data (ticks) to an external application (Python) via TCP and executes orders received in return.

Tick Streaming: Sends `Symbol`, `Time`, `Bid`, `Ask` in JSON format on every tick.
Order Execution: Receives commands (`BUY`, `SELL`, `CLOSE`) to trade directly from the external application.
Auto-Reconnect: Automatically attempts to reconnect if the TCP server (Python) is unavailable.

Commands (Python -> cBot)

Send these strings (followed by a newline `\n`) to control cTrader:

*   **Buy**: `BUY <lots> [sl_pips] [tp_pips]`
    *   Ex: `BUY 0.01` (Buy 0.01 lots, no SL/TP)
    *   Ex: `BUY 1.5 10 20` (Buy 1.5 lots, SL 10 pips, TP 20 pips)
*   **Sell**: `SELL <lots> [sl_pips] [tp_pips]`
    *   Ex: `SELL 0.1`
*   **Close**: `CLOSE`
    *   Closes all open positions matching the bot's `Position Label`.

**Python Script Example
**

Here is how to receive ticks and send a simple order (Python):


import socket
import json

HOST = '127.0.0.1'
PORT = 9001

server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
server.bind((HOST, PORT))
server.listen(1)
print(f"Waiting for cBot on {HOST}:{PORT}...")

conn, addr = server.accept()
print(f"Connected: {addr}")

while True:
    data = conn.recv(1024)
    if not data: break
    
    # Process received lines
    lines = data.decode().split('\n')
    for line in lines:
        if not line: continue
        try:
            tick = json.loads(line)
            print(f"Tick received: {tick['bid']} / {tick['ask']}")
            
            # Example: If price crosses 1.06000, buy
            if tick['bid'] > 1.06000:
                print("Sending Buy order...")
                conn.sendall(b"BUY 0.01 10 20\n")
                
        except json.JSONDecodeError:
            pass

