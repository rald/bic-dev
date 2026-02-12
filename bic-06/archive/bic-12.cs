using System;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;

public class BicForm: Form {

	public TextBox chatBox, inputBox;

	public BicForm() {
		InitializeComponent();
	}

	private void InitializeComponent() {
		Text = "bic";
		Size = new Size(320,200);

		KeyPreview = true;
		KeyDown += BicForm_KeyDown;

		chatBox = new TextBox() {
			Location = new Point(5,5),
			Size = new Size(305,135),
			Font = new Font("Monospace",12f),
			BackColor = Color.Black,
			ForeColor = Color.Lime,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
			TabStop = true,
			TabIndex = 1,
		};
		Controls.Add(chatBox);

		inputBox = new TextBox() {
			Location = new Point(5,145),
			Size = new Size(305,32),
			Font = new Font("Monospace",12f),
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

	private void SendText(String text) {
		  chatBox.AppendText(text);
		  chatBox.ScrollToCaret();
		  inputBox.Text="";
	}

	private void InputBox_KeyPress(object sender, KeyPressEventArgs e) {
		if (e.KeyChar == 13) {
		  e.Handled = true;
			SendText(inputBox.Text+Environment.NewLine);
		}
	}

	[STAThread]
	public static void Main() {
		Application.EnableVisualStyles();
		Application.Run(new BicForm());
	}

}
