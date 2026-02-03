// cTrader cBot: Stream ticks to local TCP
// Drop this cBot on a 1-tick chart.
// Sends JSON lines: {"symbol":"EURUSD","time":"2025-01-01T00:00:00.000Z","bid":1.23456,"ask":1.23478}

using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class TickTcpStreamer : Robot
    {
        [Parameter("Host", DefaultValue = "127.0.0.1")]
        public string Host { get; set; }

        [Parameter("Port", DefaultValue = 9001)]
        public int Port { get; set; }

        [Parameter("Reconnect Seconds", DefaultValue = 3)]
        public int ReconnectSeconds { get; set; }

        [Parameter("Position Label", DefaultValue = "XXX")]
        public string PositionLabel { get; set; }

        private TcpClient _client;
        private NetworkStream _stream;
        private DateTime _lastConnectAttempt;
        private long _tickCount = 0;
        private string _recvBuffer = "";

        protected override void OnStart()
        {
            Connect();
        }

        protected override void OnStop()
        {
            CloseConnection();
        }

        protected override void OnTick()
        {
            _tickCount++;

            if (!IsConnected())
            {
                if ((Server.Time - _lastConnectAttempt).TotalSeconds >= ReconnectSeconds)
                {
                    Connect();
                }
                return;
            }

            var bid = Symbol.Bid;
            var ask = Symbol.Ask;
            var timeUtc = Server.Time.ToUniversalTime().ToString("o");

            var bidStr = bid.ToString("F5", CultureInfo.InvariantCulture);
            var askStr = ask.ToString("F5", CultureInfo.InvariantCulture);
            var json = "{\"symbol\":\"" + SymbolName + "\",\"time\":\"" + timeUtc + "\",\"bid\":" + bidStr + ",\"ask\":" + askStr + "}";
            SendLine(json);

            if (_tickCount == 1 || _tickCount % 50 == 0)
            {
                Print($"Sent tick {_tickCount}: {json}");
            }

            // Handle incoming commands from Python (non-blocking)
            TryReadCommands();
        }

        private void Connect()
        {
            try
            {
                _lastConnectAttempt = Server.Time;
                CloseConnection();
                _client = new TcpClient();
                _client.Connect(Host, Port);
                _stream = _client.GetStream();
                Print($"Connected to {Host}:{Port}");
            }
            catch (Exception ex)
            {
                Print($"Connect failed: {ex.Message}");
                CloseConnection();
            }
        }

        private void SendLine(string line)
        {
            try
            {
                if (_stream == null || !_stream.CanWrite)
                {
                    CloseConnection();
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                _stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                Print($"Send failed: {ex.Message}");
                CloseConnection();
            }
        }

        private void TryReadCommands()
        {
            try
            {
                if (_stream == null || !_stream.CanRead || !_stream.DataAvailable)
                {
                    return;
                }

                var buffer = new byte[1024];
                var bytesRead = _stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    return;
                }

                _recvBuffer += Encoding.UTF8.GetString(buffer, 0, bytesRead);

                int newlineIndex;
                while ((newlineIndex = _recvBuffer.IndexOf('\n')) >= 0)
                {
                    var line = _recvBuffer.Substring(0, newlineIndex).Trim();
                    _recvBuffer = _recvBuffer.Substring(newlineIndex + 1);
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    HandleCommand(line);
                }
            }
            catch (Exception ex)
            {
                Print($"Read command failed: {ex.Message}");
            }
        }

        private void HandleCommand(string line)
        {
            try
            {
                var parts = line.Split(' ');
                var cmd = parts[0].ToUpperInvariant();
                
                // Handle CLOSE command (no parameters needed)
                if (cmd == "CLOSE")
                {
                    int closedCount = 0;
                    foreach (var position in Positions.FindAll(PositionLabel, SymbolName))
                    {
                        ClosePosition(position);
                        closedCount++;
                    }
                    Print($"Close command executed: {closedCount} position(s) closed with label '{PositionLabel}'");
                    return;
                }

                // Other commands need parameters
                if (parts.Length < 2)
                {
                    Print($"Invalid command: {line}");
                    return;
                }

                if (cmd == "BUY" || cmd == "SELL")
                {
                    if (!double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var lots))
                    {
                        Print($"Invalid lot size: {parts[1]}");
                        return;
                    }

                    double? slPips = null;
                    double? tpPips = null;
                    if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                    {
                        slPips = sl;
                    }
                    if (parts.Length >= 4 && double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                    {
                        tpPips = tp;
                    }

                    var volume = Symbol.QuantityToVolumeInUnits(lots);
                    var tradeType = cmd == "BUY" ? TradeType.Buy : TradeType.Sell;
                    ExecuteMarketOrder(tradeType, SymbolName, volume, PositionLabel, slPips, tpPips);
                    Print($"Order sent: {cmd} {lots} lots SL={slPips} TP={tpPips} Label={PositionLabel}");
                    return;
                }

                Print($"Unknown command: {line}");
            }
            catch (Exception ex)
            {
                Print($"Command error: {ex.Message}");
            }
        }

        private bool IsConnected()
        {
            return _client != null && _client.Connected && _stream != null;
        }

        private void CloseConnection()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _stream = null;
                _client = null;
            }
        }
    }
}
