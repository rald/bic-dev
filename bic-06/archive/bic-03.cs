using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Collections.Generic;

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
    private List<byte> receiveBuffer = new List<byte>();

    public BicForm() 
    {
        InitializeComponent();
    }

    private void InitializeComponent() 
    {
        Text = "Bic IRC Client";
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

        CenterToScreen();
    }

    private void Connect() 
    {
        try 
        {
            client = new TcpClient(currentServer, int.Parse(currentPort));
            client.ReceiveTimeout = 30000;
            stream = client.GetStream();
            
            isConnected = true;
            serverWelcomeReceived = false;
            receiveBuffer.Clear();
            
            SendRaw($"NICK {nickname}\r\n");
            SendRaw($"USER {nickname} 0 * :{nickname}\r\n");
            
            receiveThread = new Thread(ReceiveMessages);
            receiveThread.IsBackground = true;
            receiveThread.Start();
            
            AppendChat($"> Connected to {currentServer}:{currentPort}\n");
        } 
        catch (Exception ex) 
        {
            AppendChat($"> Error: {ex.Message}\n");
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

    // FIXED: No more Take() - direct byte copying
    private void ReceiveMessages()
    {
        byte[] buffer = new byte[4096];
        
        while (isConnected) 
        {
            try 
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                // Add new data to buffer (FIXED: Direct byte copying)
                for (int i = 0; i < bytesRead; i++)
                {
                    receiveBuffer.Add(buffer[i]);
                }
                
                // Process complete lines
                ProcessBuffer();
            } 
            catch (ThreadInterruptedException) { break; }
            catch { break; }
        }
    }

    private void ProcessBuffer()
    {
        string data = Encoding.UTF8.GetString(receiveBuffer.ToArray());
        int newlinePos;
        
        while ((newlinePos = data.IndexOf("\r\n")) >= 0)
        {
            string line = data.Substring(0, newlinePos);
            data = data.Substring(newlinePos + 2);
            
            if (!string.IsNullOrWhiteSpace(line))
            {
                ParseIRCMessage(line);
            }
        }
        
        // Keep remaining partial data
        receiveBuffer.Clear();
        if (!string.IsNullOrEmpty(data))
        {
            receiveBuffer.AddRange(Encoding.UTF8.GetBytes(data));
        }
    }

    private void ParseIRCMessage(string raw) 
    {
        if (InvokeRequired) 
        {
            Invoke(new Action<string>(ParseIRCMessage), raw);
            return;
        }

        if (string.IsNullOrWhiteSpace(raw)) return;
        
        AppendChat($">>> RAW: {raw.Trim()}\n");
        ParseSingleMessage(raw);
    }

    private void ParseSingleMessage(string raw)
    {
        if (Regex.IsMatch(raw, @"\b00[1-5]\b|\b422\b"))
        {
            serverWelcomeReceived = true;
            AppendChat(">>> SERVER WELCOME - Ready to chat!\n");
            
            if (currentTarget.StartsWith("#"))
            {
                Thread.Sleep(500);
                SendRaw($"JOIN {currentTarget}\r\n");
                AppendChat($">>> AUTO-JOIN: {currentTarget}\n");
            }
            return;
        }
        
        if (raw.Contains(" PING "))
        {
            Match pingMatch = Regex.Match(raw, @"^PING :(.*)");
            if (pingMatch.Success)
            {
                string server = pingMatch.Groups[1].Value;
                SendRaw($"PONG :{server}\r\n");
                AppendChat(">>> PONG sent\n");
            }
            return;
        }
        
        if (raw.Contains(" PRIVMSG "))
        {
            Match privmsgMatch = Regex.Match(raw, @"(?:^:([^ ]+)![^ ]+) +PRIVMSG +([^\r\n :]+) +:(.*)");
            if (privmsgMatch.Success)
            {
                string sender = privmsgMatch.Groups[1].Value;
                string target = privmsgMatch.Groups[2].Value;
                string message = privmsgMatch.Groups[3].Value;
                string displayTarget = target.StartsWith("#") ? target : "PM";
                AppendChat($">>> CHAT [{displayTarget}] <{sender}> {message}\n");
            }
        } 
        else if (raw.Contains(" JOIN "))
        {
            Match joinMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +JOIN +([^\r\n]+)");
            if (joinMatch.Success)
            {
                string sender = joinMatch.Groups[1].Value;
                string channel = joinMatch.Groups[2].Value;
                AppendChat($">>> JOIN * {sender} joined {channel}\n");
            }
        }
        else if (raw.Contains(" PART ") || raw.Contains(" QUIT "))
        {
            Match partMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +(PART|QUIT)");
            if (partMatch.Success)
            {
                string sender = partMatch.Groups[1].Value;
                AppendChat($">>> LEFT * {sender} left\n");
            }
        }
        else if (raw.Contains(" NICK "))
        {
            Match nickMatch = Regex.Match(raw, @"^:([^!]+)![^ ]+ +NICK +:?(.*)");
            if (nickMatch.Success)
            {
                string sender = nickMatch.Groups[1].Value;
                string newNick = nickMatch.Groups[2].Value;
                AppendChat($">>> NICK * {sender} is now {newNick}\n");
            }
        }
        else if (raw.Contains(" 332 "))
        {
            Match topicMatch = Regex.Match(raw, @"^:[^ ]+ +332 +[^ ]+ +([^\r\n]+)");
            if (topicMatch.Success)
            {
                AppendChat($">>> TOPIC: {topicMatch.Groups[1].Value}\n");
            }
        }
        else if (raw.Contains(" 353 ") || raw.Contains(" 366 "))
        {
            AppendChat($">>> NAMES: {raw}\n");
        }
        else if (Regex.IsMatch(raw, @"\b[0-9]{3}\b"))
        {
            AppendChat($">>> NUMERIC: {raw}\n");
        }
    }

    private void SendRaw(string raw) 
    {
        if (isConnected && stream != null) 
        {
            byte[] data = Encoding.UTF8.GetBytes(raw);
            stream.Write(data, 0, data.Length);
            stream.Flush();
            AppendChat($">>> SENT: {raw.Trim()}\n");
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
                    AppendChat($"> Server set: {currentServer}:{currentPort}\n");
                }
                break;
                
            case "/nick":
                if (parts.Length > 1) 
                {
                    nickname = parts[1];
                    if (isConnected) SendRaw($"NICK {nickname}\r\n");
                    AppendChat($"> Nick: {nickname}\n");
                }
                break;
                
            case "/target":
                if (parts.Length > 1) 
                {
                    currentTarget = parts[1];
                    AppendChat($"> Target: {currentTarget}\n");
                }
                break;
                
            case "/join":
                if (parts.Length > 1 && parts[1].StartsWith("#")) 
                {
                    currentTarget = parts[1];
                    if (isConnected && serverWelcomeReceived)
                    {
                        SendRaw($"JOIN {currentTarget}\r\n");
                    }
                    AppendChat($"> Joining {currentTarget}\n");
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
                    SendRaw($"PRIVMSG {target} :{message}\r\n");
                }
                break;

            case "/me":
                if (parts.Length > 1 && isConnected)
                {
                    string action = string.Join(" ", parts, 1, parts.Length - 1);
                    SendRaw($"PRIVMSG {currentTarget} :\x01ACTION {action}\x01\r\n");
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
                    SendRaw($"PRIVMSG {currentTarget} :{text}\r\n");
                    AppendChat($">>> YOU [{currentTarget}] <{nickname}> {text}\n");
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
