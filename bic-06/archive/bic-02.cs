using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Text.RegularExpressions;

public class BicForm : Form 
{
    private string nickname = "frio";
    private string currentServer = "irc.undernet.org";
    private string currentPort = "6667";
    private string currentTarget = "#pantasya";  

    public TextBox chatBox, inputBox;
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isConnected = false;
    private bool serverWelcomeReceived = false;

    public BicForm() 
    {
        InitializeComponent();
    }

    private void InitializeComponent() 
    {
        Text = "Bic IRC Client - Undernet (All Messages)";
        Size = new Size(600, 400);
        KeyPreview = true;
        KeyDown += BicForm_KeyDown;

        chatBox = new TextBox() 
        {
            Location = new Point(10, 10),
            Size = new Size(574, 320),
            Font = new Font("Consolas", 9f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        Controls.Add(chatBox);

        inputBox = new TextBox() 
        {
            Location = new Point(10, 340),
            Size = new Size(574, 25),
            Font = new Font("Consolas", 9f),
            BackColor = Color.FromArgb(32, 32, 32),
            ForeColor = Color.White,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };
        inputBox.KeyPress += InputBox_KeyPress;
        Controls.Add(inputBox);

        AppendChat("=== Bic IRC Client (Undernet - SHOWS ALL MESSAGES) ===\n");
        AppendChat("> /server <host:port>  /connect  /disconnect\n");
        AppendChat("> /nick <name>  /target <#chan|user>  /join <#chan>\n");
        AppendChat("> /msg <user> <text>  /me <action>\n");
        AppendChat("> ALL raw IRC messages displayed for debugging\n");

        CenterToScreen();
    }

    private void Connect() 
    {
        try 
        {
            client = new TcpClient(currentServer, int.Parse(currentPort));
            stream = client.GetStream();
            
            isConnected = true;
            serverWelcomeReceived = false;
            
            SendRaw(string.Format("NICK {0}\r\n", nickname));
            SendRaw(string.Format("USER {0} 0 * :{0}\r\n", nickname));
            
            receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            AppendChat(string.Format("> Connected to {0}:{1}\n", currentServer, currentPort));
        } 
        catch (Exception ex) 
        {
            AppendChat(string.Format("> Error: {0}\n", ex.Message));
        }
    }

    private void Disconnect() 
    {
        isConnected = false;
        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Interrupt();
            receiveThread.Join(1000);
        }
        stream?.Close();
        client?.Close();
        AppendChat("> Disconnected\n");
    }

    private void ReceiveMessages() 
    {
        byte[] buffer = new byte[4096];
        while (isConnected) 
        {
            try 
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                ParseIRCMessage(message);
            } 
            catch (ThreadInterruptedException) { break; }
            catch { break; }
        }
    }

    private void ParseIRCMessage(string raw) 
    {
        if (InvokeRequired) 
        {
            Invoke(new Action<string>(ParseIRCMessage), raw);
            return;
        }

        string[] messages = raw.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg)) continue;
            
            // *** DISPLAY ALL RAW IRC MESSAGES ***
            AppendChat(string.Format(">>> RAW: {0}\n", msg.Trim()));
            
            ParseSingleMessage(msg);
        }
    }

    private void ParseSingleMessage(string raw)
    {
        // Server welcome (001-005, 422)
        if (Regex.IsMatch(raw, @"\b00[1-5]\b|\b422\b"))
        {
            serverWelcomeReceived = true;
            AppendChat(">>> SERVER WELCOME - Ready to chat!\n");
            
            if (currentTarget.StartsWith("#"))
            {
                Thread.Sleep(500);
                SendRaw(string.Format("JOIN {0}\r\n", currentTarget));
                AppendChat(string.Format(">>> AUTO-JOIN: {0}\n", currentTarget));
            }
            return;
        }
        
        // PING/PONG (CRITICAL for staying connected)
        if (raw.Contains(" PING "))
        {
            Match pingMatch = Regex.Match(raw, @"^PING :(.*)");
            if (pingMatch.Success)
            {
                string server = pingMatch.Groups[1].Value;
                SendRaw(string.Format("PONG :{0}\r\n", server));
                AppendChat(">>> PONG sent to server\n");
            }
            return;
        }
        
        // PRIVMSG (chat messages)
        if (raw.Contains(" PRIVMSG "))
        {
            Match privmsgMatch = Regex.Match(raw, @"(?:^:([^ ]+)![^ ]+) +PRIVMSG +([^\r\n :]+) +:(.*)");
            if (privmsgMatch.Success)
            {
                string sender = privmsgMatch.Groups[1].Value;
                string target = privmsgMatch.Groups[2].Value;
                string message = privmsgMatch.Groups[3].Value;
                string displayTarget = target.StartsWith("#") ? target : "PM";
                AppendChat(string.Format(">>> CHAT [{0}] <{1}> {2}\n", displayTarget, sender, message));
            }
        } 
        // JOIN
        else if (raw.Contains(" JOIN "))
        {
            Match joinMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +JOIN +([^\r\n]+)");
            if (joinMatch.Success)
            {
                string sender = joinMatch.Groups[1].Value;
                string channel = joinMatch.Groups[2].Value;
                AppendChat(string.Format(">>> JOIN * {0} joined {1}\n", sender, channel));
            }
        }
        // PART/QUIT
        else if (raw.Contains(" PART ") || raw.Contains(" QUIT "))
        {
            Match partMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +(PART|QUIT)");
            if (partMatch.Success)
            {
                string sender = partMatch.Groups[1].Value;
                AppendChat(string.Format(">>> LEFT * {0} left\n", sender));
            }
        }
        // NICK change
        else if (raw.Contains(" NICK "))
        {
            Match nickMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +NICK +:?(.*)");
            if (nickMatch.Success)
            {
                string sender = nickMatch.Groups[1].Value;
                string newNick = nickMatch.Groups[2].Value;
                AppendChat(string.Format(">>> NICK * {0} is now {1}\n", sender, newNick));
            }
        }
        // Topic (332)
        else if (raw.Contains(" 332 "))
        {
            Match topicMatch = Regex.Match(raw, @"^:[^ ]+ +332 +[^ ]+ +([^\r\n]+)");
            if (topicMatch.Success)
            {
                AppendChat(string.Format(">>> TOPIC: {0}\n", topicMatch.Groups[1].Value));
            }
        }
        // Names list (353, 366)
        else if (raw.Contains(" 353 ") || raw.Contains(" 366 "))
        {
            AppendChat(string.Format(">>> NAMES: {0}\n", raw));
        }
        // Other numerics
        else if (Regex.IsMatch(raw, @"\b[0-9]{3}\b"))
        {
            AppendChat(string.Format(">>> NUMERIC: {0}\n", raw));
        }
    }

    private void SendRaw(string raw) 
    {
        if (isConnected && stream != null) 
        {
            byte[] data = Encoding.UTF8.GetBytes(raw);
            stream.Write(data, 0, data.Length);
            stream.Flush();
            AppendChat(string.Format(">>> SENT: {0}\n", raw.Trim()));
        }
    }

    private void HandleCommand(string cmd) 
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0].ToLower();

        switch (command) 
        {
            case "/connect":
                if (!isConnected) Connect();
                else AppendChat("> Already connected\n");
                break;
                
            case "/disconnect":
            case "/quit":
                if (isConnected) Disconnect();
                else AppendChat("> Not connected\n");
                break;
                
            case "/server":
                if (parts.Length > 1) 
                {
                    string serverPort = parts[1];
                    int colonIdx = serverPort.IndexOf(':');
                    if (colonIdx > 0) 
                    {
                        currentServer = serverPort.Substring(0, colonIdx);
                        currentPort = serverPort.Substring(colonIdx + 1);
                    } 
                    else 
                    {
                        currentServer = serverPort;
                        currentPort = "6667";
                    }
                    AppendChat(string.Format("> Server set: {0}:{1}\n", currentServer, currentPort));
                }
                break;
                
            case "/nick":
                if (parts.Length > 1) 
                {
                    nickname = parts[1];
                    if (isConnected) SendRaw(string.Format("NICK {0}\r\n", nickname));
                    AppendChat(string.Format("> Nick: {0}\n", nickname));
                }
                break;
                
            case "/target":
                if (parts.Length > 1) 
                {
                    currentTarget = parts[1];
                    AppendChat(string.Format("> Target: {0}\n", currentTarget));
                }
                break;
                
            case "/join":
                if (parts.Length > 1 && parts[1].StartsWith("#")) 
                {
                    currentTarget = parts[1];
                    if (isConnected && serverWelcomeReceived)
                    {
                        SendRaw(string.Format("JOIN {0}\r\n", currentTarget));
                    }
                    AppendChat(string.Format("> Joining {0}\n", currentTarget));
                }
                else 
                {
                    AppendChat("> Usage: /join #channel\n");
                }
                break;

            case "/msg":
                if (parts.Length > 2)
                {
                    string target = parts[1];
                    string message = string.Join(" ", parts, 2, parts.Length - 2);
                    SendRaw(string.Format("PRIVMSG {0} :{1}\r\n", target, message));
                }
                break;

            case "/me":
                if (parts.Length > 1 && isConnected)
                {
                    string action = string.Join(" ", parts, 1, parts.Length - 1);
                    SendRaw(string.Format("PRIVMSG {0} :\x01ACTION {1}\x01\r\n", currentTarget, action));
                }
                break;
        }
    }

    private void AppendChat(string text) 
    {
        if (InvokeRequired) 
        {
            Invoke(new Action<string>(AppendChat), text);
            return;
        }
        chatBox.AppendText(text);
        chatBox.SelectionStart = chatBox.Text.Length;
        chatBox.ScrollToCaret();
    }

    private void BicForm_KeyDown(object sender, KeyEventArgs e) 
    {
        if (e.KeyCode == Keys.Escape) 
        {
            Disconnect();
            Close();
        }
    }

    private void InputBox_KeyPress(object sender, KeyPressEventArgs e) 
    {
        if (e.KeyChar == 13) 
        {
            e.Handled = true;
            string text = inputBox.Text.Trim();
            if (!string.IsNullOrEmpty(text)) 
            {
                if (text.StartsWith("/")) 
                {
                    HandleCommand(text);
                } 
                else if (isConnected && serverWelcomeReceived) 
                {
                    SendRaw(string.Format("PRIVMSG {0} :{1}\r\n", currentTarget, text));
                    AppendChat(string.Format(">>> YOU [{0}] <{1}> {2}\n", currentTarget, nickname, text));
                } 
                else 
                {
                    AppendChat("> Not connected or not welcomed. Use /connect\n");
                }
            }
            inputBox.Clear();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e) 
    {
        Disconnect();
        base.OnFormClosed(e);
    }

    [STAThread]
    public static void Main() 
    {
        Application.EnableVisualStyles();
        Application.Run(new BicForm());
    }
}
