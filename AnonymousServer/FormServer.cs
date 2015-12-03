namespace AnonymousServer
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Forms;
    using AnonymousPipeWrapper;

    public partial class FormServer : Form
    {
        private readonly AnonymousPipeServer<string> _server = new AnonymousPipeServer<string>(Constants.PIPE_NAME);
        private readonly ISet<string> _clients = new HashSet<string>();

        public FormServer()
        {
            this.InitializeComponent();
            this.Load += this.OnLoad;
        }

        private void OnLoad(object sender, EventArgs eventArgs)
        {
            this._server.ClientConnected += this.OnClientConnected;
            this._server.ClientDisconnected += this.OnClientDisconnected;
            this._server.ClientMessage += (client, message) => this.AddLine("<b>" + client.Name + "</b>: " + message);
            this._server.Start();
        }

        private void OnClientConnected(AnonymousPipeConnection<string, string> connection)
        {
            this._clients.Add(connection.Name);
            this.AddLine("<b>" + connection.Name + "</b> connected!");
            this.UpdateClientList();
            connection.PushMessage("Welcome!  You are now connected to the server.");
        }

        private void OnClientDisconnected(AnonymousPipeConnection<string, string> connection)
        {
            this._clients.Remove(connection.Name);
            this.AddLine("<b>" + connection.Name + "</b> disconnected!");
            this.UpdateClientList();
        }

        private void AddLine(string html)
        {
            this.richTextBoxMessages.Invoke(new Action(delegate
                {
                    richTextBoxMessages.Text += Environment.NewLine + "<div>" + html + "</div>";
                }));
        }

        private void UpdateClientList()
        {
            this.listBoxClients.Invoke(new Action(this.UpdateClientListImpl));
        }

        private void UpdateClientListImpl()
        {
            this.listBoxClients.Items.Clear();
            foreach (var client in this._clients)
            {
                this.listBoxClients.Items.Add(client);
            }
        }

        private void buttonSend_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(this.textBoxMessage.Text))
                return;

            this._server.PushMessage(this.textBoxMessage.Text);
            this.textBoxMessage.Text = "";
        }
    }
}
