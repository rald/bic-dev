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
    private string nick = "fria";
    private string user = "fria";



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
//        AppendChat("Type /connect irc.libera.chat:6667 to start");
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
            
            AppendChat("<connected to " + serverHost + ":" + serverPort + ">");
        } catch (Exception ex) {
            AppendChat("<connect failed: " + ex.Message + ">");
        }
    }

    private void disconnect() {
        connected = false;
        if (client != null) client.Close();
        stream = null;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(1000);
        channels.Clear();
        currentTarget = null;
        AppendChat("<disconnected>");
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
            AppendChat(raw.Trim());
        }
    }

    private void ParsePrivmsg(string raw) {
        try {
            int privmsgIndex = raw.IndexOf(" PRIVMSG ");
            int colonIndex = raw.IndexOf(" :", privmsgIndex);
            
            string target = raw.Substring(privmsgIndex + 9, colonIndex - privmsgIndex - 9).Trim();
            string sender = raw.Substring(1, raw.IndexOf('!') - 1);
            string message = raw.Substring(colonIndex + 2).Trim();

            // Strip IRC color and formatting codes
            message = StripIrcCodes(message);

            string display = "[" + target + "] <" + sender + "> " + message;
            AppendChat(display);
        } catch {
            AppendChat(raw.Trim());
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
        // Remove mIRC color codes: \x03 followed by optional 1-2 digits, optional comma + 1-2 digits
        message = Regex.Replace(message, @"\x03(\d{1,2}(?:,\d{1,2})?)?", "");
        
        // Remove common formatting codes: bold, italic, underline, reverse, strikethrough, reset
        message = Regex.Replace(message, "[\x02\x0F\x12\x16\x1D\x1F]", "");
        
        // Remove other control codes (CTCP, etc.)
        message = Regex.Replace(message, "[\x01-\x09\x0B-\x1F]", "");
        
        return message;
    }

    private void AppendChat(string text) {
        if (InvokeRequired) {
            Invoke(new Action<string>(AppendChat), text);
            return;
        }
        chatBox.AppendText(text + Environment.NewLine);
        chatBox.SelectionStart = chatBox.Text.Length;
        chatBox.ScrollToCaret();
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
		                AppendChat("<" + nick + "> " + text);
		            } else if (channels.Count > 0) {
		                string target = new List<string>(channels)[0];
		                SendRaw("PRIVMSG " + target + " :" + text + "\r\n");
		                AppendChat("<" + nick + "> " + text);
		            } else {
		                AppendChat("<no target/channel set>");
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
                AppendChat("Channels: " + string.Join(", ", channels));
                break;
                
            case "/part":
                if (parts.Length > 1 && channels.Contains(parts[1])) {
                    SendRaw("PART " + parts[1] + "\r\n");
                    channels.Remove(parts[1]);
                    if (currentTarget == parts[1]) currentTarget = null;
                    AppendChat(">>> parted " + parts[1]);
                } else {
                    AppendChat("Usage: /part #channel");
                }
                break;
                
            case "/target":
                if (parts.Length > 1) {
                    string newTarget = parts[1];
                    if (newTarget.StartsWith("#") && channels.Contains(newTarget)) {
                        currentTarget = newTarget;
                        AppendChat(">>> target set to " + newTarget);
                    } else if (newTarget.Contains("!")) {
                        currentTarget = newTarget;
                        AppendChat(">>> PM target: " + newTarget);
                    } else {
                        AppendChat("Usage: /target #channel or /target user!host");
                    }
                } else {
                    AppendChat("Current target: " + (currentTarget ?? "none"));
                }
                break;
                
            case "/join":
                if (parts.Length > 1) {
                    SendRaw("JOIN " + parts[1] + "\r\n");
                    AppendChat(">>> joining " + parts[1]);
                    currentTarget = parts[1];
                }
                break;
                
            case "/nick":
                if (parts.Length > 1) {
                    nick = parts[1];
                    SendRaw("NICK " + nick + "\r\n");
                    AppendChat(">>> nick: " + nick);
                }
                break;
                
            case "/quit":
                disconnect();
                Close();
                break;
                
            case "/help":
                AppendChat("/connect host:port, /disconnect, /list, /part #chan, /target #chan|user, /join #chan, /nick name, /quit, /help");
                break;
                
            default:
                AppendChat("<unknown: " + cmd + ">");
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
