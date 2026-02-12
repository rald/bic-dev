using System;
using System.Linq;
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
            DetectUrls = true,
        };
        chatBox.LinkClicked += ChatBox_LinkClicked;
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

    private void ChatBox_LinkClicked(object sender, LinkClickedEventArgs e)
    {
        // Open the link in the default browser
        System.Diagnostics.Process.Start(e.LinkText);
    }	

    private void Connect() {
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

            AppendChat("<connecting>", Color.Yellow);
        } catch (Exception ex) {
            AppendError("<connect failed: " + ex.Message + ">");
        }
    }

    private void Disconnect() {
        connected = false;
        if (client != null) client.Close();
        stream = null;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(1000);
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
        } else if (raw.Contains(" 001 ")) {
            AppendChat("<connected to " + serverHost + ":" + serverPort + ">", Color.Green);
        } else if (raw.Contains(" 321 ")) {  // RPL_LISTSTART
            AppendSystem(">>> channel list:");
        } else if (raw.Contains(" 322 ")) {  // RPL_LIST
            ParseListResponse(raw);
        } else if (raw.Contains(" 323 ")) {  // RPL_LISTEND
            AppendSystem(">>> end of channel list");
        } else if (raw.Contains(" 353 ")) {  // NAMES list
            ParseNamesList(raw);
        } else if (raw.Contains(" 366 ")) {  // End of NAMES
            // Optional: could add a marker here if needed
        } else {
            AppendServer(raw.Trim());
        }
    }

    private void ParseListResponse(string raw) {
        try {
            string[] parts = raw.Split(' ');
            if (parts.Length < 5) return;
            
            // RPL_LIST format: "<channel> <usercount> :<topic>"
            string channel = parts[3];
            int userCount = int.Parse(parts[4]);
            string topic = raw.Substring(raw.LastIndexOf(" :") + 2).Trim();
            
            string display = $"[{userCount}] {channel} <{topic}>";
            AppendChat(display, Color.Cyan);
        } catch {
            AppendError("Failed to parse list: " + raw.Trim());
        }
    }

    private void ParseNamesList(string raw) {
        try {
            string[] parts = raw.Split(' ');
            if (parts.Length < 5) return;
            
            string channel = parts[4];  // Channel name is parameter 4 (0-based index after command params)
            string namesPart = raw.Substring(raw.IndexOf(" :", raw.IndexOf(" 353 ")) + 2).Trim();
            
            // Split names by spaces, handling nicks with spaces in prefixes by checking common IRC prefixes
            List<string> names = new List<string>();
            string currentName = "";
            
            for (int i = 0; i < namesPart.Length; i++) {
                char c = namesPart[i];
                if (c == ' ' && !currentName.EndsWith("\\")) {  // Split on space unless escaped
                    if (!string.IsNullOrEmpty(currentName)) {
                        names.Add(currentName.TrimStart(' ', '@', '+', '~', '&', '%', '!'));
                        currentName = "";
                    }
                } else {
                    currentName += c;
                }
            }
            if (!string.IsNullOrEmpty(currentName)) {
                names.Add(currentName.TrimStart(' ', '@', '+', '~', '&', '%', '!'));
            }
            
            string namesList = string.Join(", ", names);
            AppendSystem("Names " + channel + ": " + namesList);
        } catch {
            AppendError("Failed to parse names: " + raw.Trim());
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

        int maxLines = 4096;
        if (chatBox.Lines.Length > maxLines)
        {
            // Copy current lines, remove from the top
            var lines = chatBox.Lines.ToList();
            lines.RemoveRange(0, lines.Count - maxLines);
            chatBox.Lines = lines.ToArray();
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
        AppendChat(text, Color.Blue);
    }

    private void AppendUser(string text) {
        AppendChat(text, Color.White);
    }

    private void BicForm_KeyDown(object sender, KeyEventArgs e) {
        if (e.KeyCode == Keys.Escape) {
            e.Handled = true;
        
            DialogResult result = MessageBox.Show(
                "Do you want to quit?", // Message
                "Confirmation",             // Title
                MessageBoxButtons.YesNo,    // Buttons
                MessageBoxIcon.Question     // Icon
            );

            if (result == DialogResult.Yes)
            {
                Disconnect();
                Close();
            }
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
                Connect();
                break;

            case "/disconnect":
                Disconnect();
                break;

            case "/part":
                if (parts.Length > 1) {
                    SendRaw("PART " + parts[1] + "\r\n");
                    AppendSystem(">>> parted " + parts[1]);
                } else {
                    AppendError("Usage: /part #channel");
                }
                break;

            case "/target":
                if (parts.Length > 1) {
                    string newTarget = parts[1];
                    // Allow: #channel, or plain nickname (no !host allowed anymore)
                    if (newTarget.StartsWith("#")) {
                        currentTarget = newTarget;
                        AppendSystem(">>> target set to " + newTarget);
                    } else {
                        // Treat as PM target: just a nick, not user!host
                        currentTarget = newTarget;
                        AppendSystem(">>> PM target: " + newTarget);
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

            case "/names":
                if (parts.Length > 1) {
                    string channel = parts[1];
                    SendRaw("NAMES " + channel + "\r\n");
                    AppendSystem(">>> requesting names for " + channel);
                } else {
                    AppendError("Usage: /names <#channel>");
                }
                break;

            case "/list":
                SendRaw("LIST\r\n");
                AppendSystem(">>> requesting channel list...");
                break;

            case "/msg":
                if (parts.Length > 2) {
                    string target = parts[1];
                    string message = string.Join(" ", parts, 2, parts.Length - 2);
                    SendRaw("PRIVMSG " + target + " :" + message + "\r\n");
                    AppendUser("->" + target + " <" + nick + "> " + message);
                    AppendSystem(">>> msg sent to " + target);
                } else {
                    AppendError("Usage: /msg <#channel|user> message");
                }
                break;

            case "/clear":
                chatBox.Clear();
                break;

            case "/quit":
                string quitMessage = "Bic IRC Client";
                if (parts.Length > 1) {
                    quitMessage = string.Join(" ", parts, 1, parts.Length - 1);
                }
                SendRaw("QUIT :" + quitMessage + "\r\n");
                Disconnect();
                Close();
                break;

            default:
                AppendError("<unknown: " + cmd + ">");
                break;
        }
    }

    protected override void Dispose(bool disposing) {
        Disconnect();
        base.Dispose(disposing);
    }

    [STAThread]
    public static void Main() {
        Application.Run(new BicForm());
    }
}
