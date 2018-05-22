using System;
using System.Collections;
using System.Text;
using System.Windows.Forms;


using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace GSM
{
    public partial class Form1 : Form
    {
        private PCSC.SIMCard _simcard;

        public Form1()
        {
            InitializeComponent();

        }


        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (this._simcard != null)
                this._simcard.Dispose();

            this.backgroundWorker1.CancelAsync();
        }

        private void Form1_DoubleClick(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                this._simcard = new PCSC.SIMCard();

                this.comboBox1.Items.AddRange(this._simcard.Devices as object[]);
                if (this.comboBox1.Items.Count > 0)
                {
                    this.comboBox1.SelectedIndex = 0;
                    this.button2.Enabled = true;
                }

                this._simcard.OnSIMCardInserted += _simcard_OnSIMCardInserted;
                this._simcard.OnSIMCardRemoved += _simcard_OnSIMCardRemoved;
            }
            catch { }
        }

        private void _simcard_OnSIMCardRemoved(object sender, PCSC.SIMCardRemovedEventArgs e)
        {
            //MessageBox.Show("good bye : " + e.Reader);
        }

        private void _simcard_OnSIMCardInserted(object sender, PCSC.SIMCardInsertedEventArgs e)
        {
            //MessageBox.Show("hello : " + e.Reader);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                string s_imsi = string.Empty;

                System.ComponentModel.BackgroundWorker work = new System.ComponentModel.BackgroundWorker();
                work.ProgressChanged += delegate (object me, System.ComponentModel.ProgressChangedEventArgs ev)
                {
                    this.Invoke(new MethodInvoker(delegate
                    {
                        this.toolStripProgressBar1.Value = ev.ProgressPercentage;
                    }));
                };

                work.WorkerReportsProgress = true;
                System.Collections.Generic.List<PCSC.GSMAlgorithm> gsmresult = new System.Collections.Generic.List<PCSC.GSMAlgorithm>();

                this._simcard.Connect(this.comboBox1.SelectedItem);

                if (this._simcard.Connected)
                {
                    ((Button)sender).Enabled = false;
                    work.ReportProgress(10);
                    Form2 pinCode = new Form2();

                    int pinrequired = -1;
                    while (pinrequired < 0)
                    {
                        try
                        {
                            pinrequired = Convert.ToInt32(this._simcard.Authenticated);
                            System.Threading.Thread.Sleep(100);
                        }
                        catch { }
                    }

                    if (pinrequired == 0)
                    {
                        work.ReportProgress(20);
                        pinCode.FormClosed += delegate (object form_, FormClosedEventArgs ev_)
                        {
                            Form f = form_ as Form;

                            if (f.DialogResult == DialogResult.OK)
                            {
                                byte[] pincode = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };
                                byte[] received_pincode = Encoding.ASCII.GetBytes(f.Controls["textBox1"].Text);

                                for (int i = 0; i < received_pincode.Length; i++)
                                    pincode[i] = received_pincode[i];

                                this._simcard.PIN1 = pincode;
                            }
                        };

                        while (!this._simcard.Authenticated && this._simcard.TryPIN1 > 0 && pinCode.DialogResult != DialogResult.Cancel)
                        {
                            pinCode.Text = string.Format("Code PIN - {0} essais", this._simcard.TryPIN1);
                            pinCode.Controls["textBox1"].ResetText();
                            pinCode.ShowDialog(this);
                        }

                        if (this._simcard.Authenticated)
                        {
                            work.ReportProgress(30);
                            /*
                            PCSC.Rand rand = new PCSC.Rand();
                            PCSC.GSMAlgorithm response = this._simcard.RunGSMAlgorithm(rand.ToByteArray());

                            this.textBox1.Lines = new string[]
                            {
                                "IMSI = " + Core.Utility.ByteArray.ToString(this._simcard.IMSI),
                                "RAND = " + rand.ToString(),
                                "SRES = " + Core.Utility.ByteArray.ToString(response.SRes),
                                "KC   = " + Core.Utility.ByteArray.ToString(response.Kc),
                            };
                            */
                            s_imsi = Core.Utility.ByteArray.ToString(this._simcard.IMSI);
                            for (int i = 0; i < 3; i++)
                                gsmresult.Add(this._simcard.RunGSMAlgorithm(new PCSC.Rand().ToByteArray()));

                            work.ReportProgress(40);
                        }
                    }
                    else
                    {
                        /*         
                        PCSC.Rand rand = new PCSC.Rand();
                        PCSC.GSMAlgorithm response = this._simcard.RunGSMAlgorithm(rand.ToByteArray());

                        this.textBox1.Lines = new string[]
                        {
                                "IMSI = " + Core.Utility.ByteArray.ToString(this._simcard.IMSI),
                                "RAND = " + rand.ToString(),
                                "SRES = " + Core.Utility.ByteArray.ToString(response.SRes),
                                "KC   = " + Core.Utility.ByteArray.ToString(response.Kc),
                        };
                        */
                        s_imsi = Core.Utility.ByteArray.ToString(this._simcard.IMSI);
                        for (int i = 0; i < 3; i++)
                            gsmresult.Add(this._simcard.RunGSMAlgorithm(new PCSC.Rand().ToByteArray()));

                        work.ReportProgress(40);
                    }

                    s_imsi = string.Format("{0}@wlan.mnc{2}.mcc{1}.3gppnetwork.org", s_imsi, s_imsi.Substring(1, 3).PadLeft(3, '0'), s_imsi.Substring(4, 2).PadLeft(3, '0'));
                    object[] prm = new object[] { s_imsi, gsmresult };

                    work.DoWork += new System.ComponentModel.DoWorkEventHandler(delegate (object me, System.ComponentModel.DoWorkEventArgs ev)
                    {
                        try
                        {
                            object[] args = ev.Argument as object[];
                            IniFile config = new IniFile("config.ini");
                            MySql.Data.MySqlClient.MySqlConnection sqlconn = new MySql.Data.MySqlClient.MySqlConnection()
                            {
                                ConnectionString = string.Format("server={0};uid={1};pwd={2};database={3}", config.Read("SQLHOST", "config"), config.Read("SQLUSER", "config"), Encoding.ASCII.GetString(Convert.FromBase64String(config.Read("SQLPWD", "config"))), config.Read("SQLDB", "config"))
                            };
                            sqlconn.Open();

                            System.Data.DataSet ds = new System.Data.DataSet();
                            MySql.Data.MySqlClient.MySqlDataAdapter da = new MySql.Data.MySqlClient.MySqlDataAdapter(string.Format("SELECT username,attribute,op,value FROM radcheck WHERE username = '{0}'", args[0]), sqlconn);
                            da.Fill(ds);

                            ((System.ComponentModel.BackgroundWorker)me).ReportProgress(45);

                            if (ds.Tables[0].Rows.Count == 0)
                            {
                                ds.Tables[0].Rows.Add(new object[] { args[0], "Auth-Type", ":=", "eap" });
                                ds.Tables[0].Rows.Add(new object[] { args[0], "EAP-Type", ":=", "sim" });
                                ((System.ComponentModel.BackgroundWorker)me).ReportProgress(50);

                                for (int k = 0; k < 3; k++)
                                {
                                    ds.Tables[0].Rows.Add(new object[] { args[0], string.Format("EAP-Sim-RAND{0}", k + 1), ":=", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[k].Rand).ToLower()) });
                                    ds.Tables[0].Rows.Add(new object[] { args[0], string.Format("EAP-Sim-SRES{0}", k + 1), ":=", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[k].SRes).ToLower()) });
                                    ds.Tables[0].Rows.Add(new object[] { args[0], string.Format("EAP-Sim-KC{0}", k + 1), ":=", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[k].Kc).ToLower()) });

                                    ((System.ComponentModel.BackgroundWorker)me).ReportProgress(50 + ((k + 1) * 10));
                                }
                                new MySql.Data.MySqlClient.MySqlCommandBuilder(da);
                                da.Update(ds);
                                ((System.ComponentModel.BackgroundWorker)me).ReportProgress(90);

                                ev.Result = "La carte SIM a correctement été ajoutée pour l'authentification EAP-SIM !";
                            }
                            else
                            {
                                ev.Result = "La carte SIM a déjà été enregistrée pour l'authentification EAP-SIM !";
                            }
                            /*
                            MySql.Data.MySqlClient.MySqlCommand ins = new MySql.Data.MySqlClient.MySqlCommand()
                            {
                                CommandText = string.Format("INSERT INTO radcheck(username,attribute,op,value) VALUES (@username, @attribute, @op, @value);"),
                                Connection = sqlconn
                            };
                            ins.Prepare();
                            
                            ins.Parameters.AddWithValue("@username", args[0]);
                            ins.Parameters.AddWithValue("@attribute", "Auth-Type");
                            ins.Parameters.AddWithValue("@op", ":=");
                            ins.Parameters.AddWithValue("@value", "eap");                           

                            ins.ExecuteNonQuery();

                            ins = new MySql.Data.MySqlClient.MySqlCommand()
                            {
                                CommandText = string.Format("INSERT INTO radcheck(username,attribute,op,value) VALUES (@username, @attribute, @op, @value);"),
                                Connection = sqlconn
                            };
                            ins.Prepare();

                            ins.Parameters.AddWithValue("@username", args[0]);
                            ins.Parameters.AddWithValue("@attribute", "EAP-Type");
                            ins.Parameters.AddWithValue("@op", ":=");
                            ins.Parameters.AddWithValue("@value", "sim");

                            ins.ExecuteNonQuery();

                            for (int o = 0; o < 3; o++)
                            {
                                ins = new MySql.Data.MySqlClient.MySqlCommand()
                                {
                                    CommandText = string.Format("INSERT INTO radcheck(username,attribute,op,value) VALUES (@username, @attribute, @op, @value);"),
                                    Connection = sqlconn
                                };
                                ins.Prepare();

                                ins.Parameters.AddWithValue("@username", args[0]);
                                ins.Parameters.AddWithValue("@attribute", string.Format("EAP-Sim-RAND{0}", o + 1));
                                ins.Parameters.AddWithValue("@op", ":=");
                                ins.Parameters.AddWithValue("@value", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[o].Rand).ToLower()));

                                ins.ExecuteNonQuery();

                                ins = new MySql.Data.MySqlClient.MySqlCommand()
                                {
                                    CommandText = string.Format("INSERT INTO radcheck(username,attribute,op,value) VALUES (@username, @attribute, @op, @value);"),
                                    Connection = sqlconn
                                };
                                ins.Prepare();

                                ins.Parameters.AddWithValue("@username", args[0]);
                                ins.Parameters.AddWithValue("@attribute", string.Format("EAP-Sim-SRES{0}", o + 1));
                                ins.Parameters.AddWithValue("@op", ":=");
                                ins.Parameters.AddWithValue("@value", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[o].SRes).ToLower()));

                                ins.ExecuteNonQuery();

                                ins = new MySql.Data.MySqlClient.MySqlCommand()
                                {
                                    CommandText = string.Format("INSERT INTO radcheck(username,attribute,op,value) VALUES (@username, @attribute, @op, @value);"),
                                    Connection = sqlconn
                                };
                                ins.Prepare();

                                ins.Parameters.AddWithValue("@username", args[0]);
                                ins.Parameters.AddWithValue("@attribute", string.Format("EAP-Sim-KC{0}", o + 1));
                                ins.Parameters.AddWithValue("@op", ":=");
                                ins.Parameters.AddWithValue("@value", string.Format("0x{0}", Core.Utility.ByteArray.ToString(((System.Collections.Generic.IList<PCSC.GSMAlgorithm>)args[1])[o].Kc).ToLower()));

                                ins.ExecuteNonQuery();
                            }
                            */
                            sqlconn.Close();
                            ((System.ComponentModel.BackgroundWorker)me).ReportProgress(100);
                        }
                        catch (Exception ex) { MessageBox.Show(ex.Message, ex.Source); ev.Result = "La carte SIM n'a pas pu être enregistrée pour l'authentification EAP-SIM !"; }
                    });

                    work.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(delegate (object me, System.ComponentModel.RunWorkerCompletedEventArgs ev)
                    {
                        this.textBox1.Invoke(new MethodInvoker(delegate
                        {
                            this.textBox1.Lines = new string[]
                            {
                                ev.Result.ToString(),
                                "Vous pouvez retirer la carte SIM du lecteur.",
                                Environment.NewLine,
                                "IMSI : " + s_imsi.Substring(1)
                            };

                            this._simcard.Dispose();
                            pinCode = null;
                            if (!this._simcard.Connected)
                                ((Button)sender).Enabled = true;

                            this.button1_Click(sender, e);
                        }));
                    });
                    work.RunWorkerAsync(prm);
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void Work_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //if (!this._simcard.Connected && !this.backgroundWorker1.IsBusy)
            //  this.backgroundWorker1.RunWorkerAsync();
        }

        private void backgroundWorker1_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            try
            {
                e.Result = this._simcard.Devices;
            }
            catch { e.Result = null; }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (e.Result != null)
            {
                try
                {
                    this.Invoke(new MethodInvoker(delegate
                    {
                        this.comboBox1.Items.Clear();
                        this.comboBox1.ResetText();
                        foreach (object device in e.Result as object[])
                            this.comboBox1.Items.AddRange(e.Result as object[]);

                        this.comboBox1.SelectedIndex = 0;
                    }));
                }
                catch { }
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            this.Invoke(new MethodInvoker(delegate
            {
                this.toolStripProgressBar1.Value = e.ProgressPercentage;
            }));
        }
    }



    // Change this to match your program's normal namespace

    class IniFile   // revision 10
    {
        string Path;
        string EXE = Assembly.GetExecutingAssembly().GetName().Name;

        [DllImport("kernel32")]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32")]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public IniFile(string IniPath = null)
        {
            Path = new FileInfo(IniPath ?? EXE + ".ini").FullName.ToString();
        }

        public string Read(string Key, string Section = null)
        {
            var RetVal = new StringBuilder(255);
            GetPrivateProfileString(Section ?? EXE, Key, "", RetVal, 255, Path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = null)
        {
            WritePrivateProfileString(Section ?? EXE, Key, Value, Path);
        }

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section ?? EXE);
        }

        public void DeleteSection(string Section = null)
        {
            Write(null, null, Section ?? EXE);
        }

        public bool KeyExists(string Key, string Section = null)
        {
            return Read(Key, Section).Length > 0;
        }
    }
}

