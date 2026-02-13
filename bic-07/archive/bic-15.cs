using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class BicForm: Form {

    private string serverHost = "irc.undernet.org";
    private int serverPort = 6667;
    private string nick = "frio";
    private string user = "frio";

    public RichTextBox chatBox;
    public TextBox inputBox;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool connected = false;
    private HashSet<string> channels = new HashSet<string>();
    private string currentTarget;

    public BicForm() {
        InitializeComponent();
    }

    private void InitializeComponent() {
        Text = "bic";
        Size = new Size(320,200);

        KeyPreview = true;
        KeyDown += BicForm_KeyDown;

        chatBox = new RichTextBox() {
            Location = new Point(5,5),
            Size = new Size(305,135),
            Font = new Font("Consolas",11f),
            BackColor = Color.Black,
            ForeColor = Color.LimeGreen,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TabStop = true,
            TabIndex = 1,
        };
        Controls.Add(chatBox);

        inputBox = new TextBox() {
            Location = new Point(5,145),
            Size = new Size(305,32),
            Font = new Font("Consolas",11f),
            BackColor = Color.Black,
            ForeColor = Color.White,
            Multiline = false,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            TabStop = true,
            TabIndex = 0,
        };
        inputBox.KeyPress += InputBox_KeyPress;
        Controls.Add(inputBox);

        CenterToScreen();
    }

    private void connect() {
        try {
            if (client != null) client.Close();
            client = new TcpClient(serverHost, serverPort);
            stream = client.GetStream();
            connected = true;
            
            SendRaw("NICK " + nick + "\r\n");
            SendRaw("USER " + user + " 0 * :Bic IRC Client\r\n");
            
            receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            AppendChat("<connected to " + serverHost + ":" + serverPort + ">", Color.Green);
        } catch (Exception ex) {
            AppendError("<connect failed: " + ex.Message + ">");
        }
    }

    private void disconnect() {
        connected = false;
        if (client != null) client.Close();
        stream = null;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(1000);
        channels.Clear();
        currentTarget = null;
        AppendChat("<disconnected>", Color.Yellow);
    }

    private void SendRaw(string message) {
        if (connected && stream != null) {
            byte[] data = Encoding.UTF8.GetBytes(message);
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
    }

    private void ReceiveMessages() {
        byte[] buffer = new byte[4096];
        while (connected) {
            try {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                string[] lines = message.Split(new char[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (string line in lines) {
                    if (!string.IsNullOrEmpty(line)) {
                        ParseIRCMessage(line);
                    }
                }
            } catch {
                break;
            }
        }
        connected = false;
    }

    private void ParseIRCMessage(string raw) {
        if (InvokeRequired) {
            Invoke(new Action<string>(ParseIRCMessage), raw);
            return;
        }

        if (raw.StartsWith("PING")) {
            SendRaw(raw.Replace("PING", "PONG") + "\r\n");
            return;
        }

        if (raw.Contains(" PRIVMSG ")) {
            ParsePrivmsg(raw);
        } else if (raw.Contains(" 353 ")) {
            ParseNames(raw);
        } else if (raw.Contains("JOIN")) {
            string[] parts = raw.Split(' ');
            if (parts.Length > 2) {
                string channel = parts[2];
                if (!channels.Contains(channel)) channels.Add(channel);
            }
        } else if (raw.Contains("PART") || raw.Contains("KICK")) {
            string[] parts = raw.Split(' ');
            if (parts.Length > 2) {
                string channel = parts[2];
                channels.Remove(channel);
            }
        } else {
            AppendServer(raw.Trim());
        }
    }

    private void ParsePrivmsg(string raw) {
        try {
            int privmsgIndex = raw.IndexOf(" PRIVMSG ");
            int colonIndex = raw.IndexOf(" :", privmsgIndex);
            
            string target = raw.Substring(privmsgIndex + 9, colonIndex - privmsgIndex - 9).Trim();
            string sender = raw.Substring(1, raw.IndexOf('!') - 1);
            string message = raw.Substring(colonIndex + 2).Trim();

            message = StripIrcCodes(message);

            string display = "[" + target + "] <" + sender + "> " + message;
            AppendChat(display, Color.White);
        } catch {
            AppendError(raw.Trim());
        }
    }

    private void ParseNames(string raw) {
        try {
            string[] parts = raw.Split(' ');
            string channel = parts[4];
            if (!channels.Contains(channel)) channels.Add(channel);
        } catch { }
    }

    private string StripIrcCodes(string message) {
        message = Regex.Replace(message, @"\x03(\d{1,2}(?:,\d{1,2})?)?", "");
        message = Regex.Replace(message, "[\x02\x0F\x12\x16\x1D\x1F]", "");
        message = Regex.Replace(message, "[\x01-\x09\x0B-\x1F]", "");
        return message;
    }

    private void AppendChat(string text, Color foreColor = default(Color)) {
        if (InvokeRequired) {
            Invoke(new Action<string, Color>(AppendChat), text, foreColor);
            return;
        }
        
        if (foreColor == default(Color)) 
            foreColor = Color.LimeGreen;
        
        int start = chatBox.TextLength;
        chatBox.AppendText(text + Environment.NewLine);
        
        chatBox.Select(start, text.Length);
        chatBox.SelectionColor = foreColor;
        chatBox.SelectionLength = 0;
        
        chatBox.SelectionStart = chatBox.Text.Length;
        chatBox.ScrollToCaret();
    }

    private void AppendChat(string text) {
        AppendChat(text, Color.LimeGreen);
    }

    private void AppendSystem(string text) {
        AppendChat(text, Color.Yellow);
    }

    private void AppendError(string text) {
        AppendChat(text, Color.Red);
    }

    private void AppendServer(string text) {
        AppendChat(text, Color.Cyan);
    }

    private void AppendUser(string text) {
        AppendChat(text, Color.White);
    }

    private void BicForm_KeyDown(object sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Escape) {
            disconnect();
            Close();
        }
    }

    private void InputBox_KeyPress(object sender, KeyPressEventArgs e) {
        if (e.KeyChar == 13) {
            e.Handled = true;
            string text = inputBox.Text.Trim();
            if (!string.IsNullOrEmpty(text)) {
                if (text.StartsWith("/")) {
                    HandleCommand(text);
                } else if (connected) {
                    if (!string.IsNullOrEmpty(currentTarget)) {
                        SendRaw("PRIVMSG " + currentTarget + " :" + text + "\r\n");
                        AppendUser("[" + currentTarget + "] <" + nick + "> " + text);
                    } else if (channels.Count > 0) {
                        string target = new List<string>(channels)[0];
                        SendRaw("PRIVMSG " + target + " :" + text + "\r\n");
                        AppendUser("[" + target + "] <" + nick + "> " + text);
                    } else {
                        AppendError("<no target/channel set>");
                    }
                }
            }
            inputBox.Text = "";
        }
    }

    private void HandleCommand(string cmd) {
        var parts = cmd.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLower();

        switch (command) {
            case "/connect":
                if (parts.Length > 1) {
                    string[] hostport = parts[1].Split(':');
                    serverHost = hostport[0];
                    serverPort = hostport.Length > 1 ? int.Parse(hostport[1]) : 6667;
                }
                connect();
                break;
                
            case "/disconnect":
                disconnect();
                break;
                
            case "/list":
                AppendSystem("Channels: " + string.Join(", ", channels));
                break;
                
            case "/part":
                if (parts.Length > 1 && channels.Contains(parts[1])) {
                    SendRaw("PART " + parts[1] + "\r\n");
                    channels.Remove(parts[1]);
                    if (currentTarget == parts[1]) currentTarget = null;
                    AppendSystem(">>> parted " + parts[1]);
                } else {
                    AppendError("Usage: /part #channel");
                }
                break;
                
            case "/target":
                if (parts.Length > 1) {
                    string newTarget = parts[1];
                    if (newTarget.StartsWith("#") && channels.Contains(newTarget)) {
                        currentTarget = newTarget;
                        AppendSystem(">>> target set to " + newTarget);
                    } else if (newTarget.Contains("!")) {
                        currentTarget = newTarget;
                        AppendSystem(">>> PM target: " + newTarget);
                    } else {
                        AppendError("Usage: /target #channel or /target user!host");
                    }
                } else {
                    AppendSystem("Current target: " + (currentTarget ?? "none"));
                }
                break;
                
            case "/join":
                if (parts.Length > 1) {
                    SendRaw("JOIN " + parts[1] + "\r\n");
                    AppendSystem(">>> joining " + parts[1]);
                    currentTarget = parts[1];
                }
                break;
                
            case "/nick":
                if (parts.Length > 1) {
                    nick = parts[1];
                    SendRaw("NICK " + nick + "\r\n");
                    AppendSystem(">>> nick: " + nick);
                }
                break;
                
			case "/quit":
		        string quitMessage = "Bic IRC Client";
		        SendRaw("QUIT :" + quitMessage + "\r\n");
		        disconnect();
		        Close();
		        break;
                
            case "/help":
                AppendSystem("/connect host:port, /disconnect, /list, /part #chan, /target #chan|user, /join #chan, /nick name, /quit, /help");
                break;
                
            default:
                AppendError("<unknown: " + cmd + ">");
                break;
        }
    }

    protected override void Dispose(bool disposing) {
        disconnect();
        base.Dispose(disposing);
    }

    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BicForm());
    }
}
