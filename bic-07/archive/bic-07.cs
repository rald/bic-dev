using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Collections.Generic;

public class BicForm: Form {

    private string serverHost = "irc.undernet.org";
    private int serverPort = 6667;
    private string nick = "frio";
    private string currentChannel = "#pantasya";
    private string currentTarget = "#pantasya";  // NEW: Current target for commands
    private bool connected = false;

    public RichTextBox chatBox;
    public TextBox inputBox;
    
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;

    public BicForm() {
        InitializeComponent();
//        ConnectToServer();
    }

    private void ConnectToServer() {
        try {
            client = new TcpClient(serverHost, serverPort);
            stream = client.GetStream();
            
            SendText("Connecting to " + serverHost + ":" + serverPort + "\r\n", Color.Yellow);
            
            // Send NICK and USER in correct order, no JOIN yet
            SendRaw("NICK " + nick);
            SendRaw("USER " + nick + " 0 * :Bic IRC Client");
            
            receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
        } catch (Exception ex) {
            SendText("Connection failed: " + ex.Message + "\r\n", Color.Red);
        }
    }

    private void ReceiveMessages() {
        byte[] buffer = new byte[4096];
        while (client.Connected) {
            try {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                ProcessIRCMessage(message);
            } catch {
                break;
            }
        }
    }

    private void ProcessIRCMessage(string rawMessage) {
        if (InvokeRequired) {
            Invoke(new Action<string>(ProcessIRCMessage), rawMessage);
            return;
        }

        string[] lines = rawMessage.Split('\r', '\n');
        foreach (string line in lines) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Display ALL raw IRC messages
            SendText("> " + line.Trim() + "\r\n", Color.Gray);
            
            // Handle PING/PONG
            if (line.Contains("PING")) {
                string pong = line.Replace("PING", "PONG");
                SendRaw(pong);
                continue;
            }
            
            // Wait for 001 welcome message before joining channel
            if (line.Contains(" 001 ") && !connected) {
                connected = true;
                SendRaw("JOIN " + currentChannel);
                SendText("*** Auto-joining " + currentChannel + "\r\n", Color.Yellow);
            }
            
            // Parse PRIVMSG for nicer display
            if (line.Contains(" PRIVMSG ")) {
                int privmsgIndex = line.IndexOf(" PRIVMSG ");
                int colonIndex = line.IndexOf(":", privmsgIndex);
                if (colonIndex > 0) {
                    string sender = ParseSender(line);
                    string target = line.Substring(privmsgIndex + 9, colonIndex - privmsgIndex - 9).Trim();
                    string msg = line.Substring(colonIndex + 1).Trim();
                    
                    string displayMsg = $"[{sender}] <{target}> {msg}\r\n";
                    SendText(displayMsg, Color.Cyan);
                }
            }
        }
    }

    private string ParseSender(string line) {
        int exclamation = line.IndexOf('!');
        if (exclamation > 0) {
            return line.Substring(1, exclamation - 1);
        }
        return "server";
    }

    private void SendRaw(string message) {
        if (stream != null && client.Connected) {
            byte[] data = Encoding.UTF8.GetBytes(message + "\r\n");
            stream.Write(data, 0, data.Length);
            SendText("< " + message + "\r\n", Color.Green);
        }
    }

    private void SendText(String text, Color color) {
        if (InvokeRequired) {
            Invoke(new Action<string, Color>(SendText), text, color);
            return;
        }
        
        chatBox.SelectionStart = chatBox.TextLength;
        chatBox.SelectionLength = 0;
        chatBox.SelectionColor = color;
        chatBox.AppendText(text);
        chatBox.SelectionColor = Color.Lime;
        chatBox.ScrollToCaret();
    }

    private void ProcessCommand(string input) {
        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLower();

        switch (cmd) {
            case "target":
                if (parts.Length >= 2) {
                    currentTarget = parts[1];
                    SendText($"*** Target set to {currentTarget}\r\n", Color.Yellow);
                }
                break;
            case "connect":
                if (parts.Length >= 3) {
                    serverHost = parts[1];
                    serverPort = int.Parse(parts[2]);
                    client?.Close();
                    connected = false;
                    ConnectToServer();
                }
                break;
            case "nick":
                if (parts.Length >= 2) {
                    nick = parts[1];
                    SendRaw("NICK " + nick);
                }
                break;
            case "join":
                string joinTarget = parts.Length >= 2 ? parts[1] : currentTarget;
                SendRaw($"JOIN {joinTarget}");
                if (parts.Length >= 2) currentTarget = joinTarget;
                break;
            case "part":
                string partTarget = parts.Length >= 2 ? parts[1] : currentTarget;
                SendRaw($"PART {partTarget}");
                break;
            case "msg":
            case "notice":
                if (parts.Length >= 3) {
                    string target = parts[1];
                    string message = string.Join(" ", parts, 2, parts.Length - 2);
                    string ircCmd = cmd == "msg" ? "PRIVMSG" : "NOTICE";
                    SendRaw($"{ircCmd} {target} :{message}");
                    currentTarget = target;
                } else if (parts.Length >= 2) {
                    currentTarget = parts[1];
                    SendText($"*** Target set to {currentTarget}\r\n", Color.Yellow);
                }
                break;
            case "me":
                if (parts.Length >= 2) {
                    string action = string.Join(" ", parts, 1, parts.Length - 1);
                    SendRaw($"PRIVMSG {currentTarget} :\x01ACTION {action}\x01");
                }
                break;
            case "kick":
                if (parts.Length >= 3) {
                    string channel = parts[1];
                    string user = parts[2];
                    string reason = parts.Length > 3 ? string.Join(" ", parts, 3, parts.Length - 3) : "kicked";
                    SendRaw($"KICK {channel} {user} :{reason}");
                    currentTarget = channel;
                }
                break;
            case "mode":
                if (parts.Length >= 2) {
                    string target = parts.Length >= 2 ? parts[1] : currentTarget;
                    string modes = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "";
                    SendRaw($"MODE {target} {modes}");
                    currentTarget = target;
                }
                break;
            case "topic":
                if (parts.Length >= 2) {
                    string channel = parts[1];
                    string topicText = parts.Length > 2 ? string.Join(" ", parts, 2, parts.Length - 2) : "";
                    if (!string.IsNullOrEmpty(topicText)) {
                        SendRaw($"TOPIC {channel} :{topicText}");
                    } else {
                        SendRaw($"TOPIC {channel}");
                    }
                    currentTarget = channel;
                }
                break;
            case "names":
                string namesTarget = parts.Length >= 2 ? parts[1] : currentTarget;
                SendRaw($"NAMES {namesTarget}");
                break;
			case "quit": {
				string reason = "";
				if (parts.Length >= 2) {
					reason = string.Join(" ", parts, 1, parts.Length - 1);
				}
				SendRaw("QUIT :" + reason);
				Close();
				break;
			}
            default:
                // Regular message to current target
                SendRaw($"PRIVMSG {currentTarget} :{input}");
                break;
        }
    }

    private void InitializeComponent() {
        Text = "bic - IRC Client";
        Size = new Size(600, 400);

        KeyPreview = true;
        KeyDown += BicForm_KeyDown;

        chatBox = new RichTextBox() {
            Location = new Point(5, 5),
            Size = new Size(585, 340),
            Font = new Font("Consolas", 9f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TabStop = false,
            TabIndex = 1,
        };
        Controls.Add(chatBox);

        inputBox = new TextBox() {
            Location = new Point(5, 350),
            Size = new Size(585, 25),
            Font = new Font("Consolas", 10f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            Multiline = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TabStop = true,
            TabIndex = 0,
        };
        inputBox.KeyPress += InputBox_KeyPress;
        Controls.Add(inputBox);

        CenterToScreen();
    }

    private void BicForm_KeyDown(object sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Escape) {
            Close();
        }
    }

    private void InputBox_KeyPress(object sender, KeyPressEventArgs e) {
        if (e.KeyChar == 13) {
            e.Handled = true;
            string input = inputBox.Text.Trim();
            inputBox.Text = "";
            
            if (!string.IsNullOrEmpty(input)) {
                if (input.StartsWith("/")) {
                    // Remove the / and process as command
                    ProcessCommand(input.Substring(1));
                } else {
                    // Regular chat message to current target
                    SendRaw($"PRIVMSG {currentTarget} :{input}");
                }
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e) {
        try {
            SendRaw("QUIT :Bic Client");
            client?.Close();
        } catch { }
        base.OnFormClosing(e);
    }

    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.Run(new BicForm());
    }
}
