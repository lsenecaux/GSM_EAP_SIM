using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core.Smartcard;
using Core.Utility;

namespace PCSC
{
    public delegate void SIMCardInsertedEventHandler(object sender, SIMCardInsertedEventArgs e);
    public delegate void SIMCardRemovedEventHandler(object sender, SIMCardRemovedEventArgs e);

    public class SIMCard 
    {
        private CardBase _card;

        public event SIMCardInsertedEventHandler OnSIMCardInserted = null;
        public event SIMCardRemovedEventHandler OnSIMCardRemoved = null;

        public SIMCard()
        {
            this._card = new CardNative();
            this._card.OnCardInserted += _card_OnCardInserted;
            this._card.OnCardRemoved += _card_OnCardRemoved;

            this.Connected = false;
        }

        private void _card_OnCardRemoved(object sender, string reader)
        {
            if (this.OnSIMCardRemoved != null)
                this.OnSIMCardRemoved(this, new SIMCardRemovedEventArgs(reader));
        }

        private void _card_OnCardInserted(object sender, string reader)
        {
            if (this.OnSIMCardInserted != null)
                this.OnSIMCardInserted(this, new SIMCardInsertedEventArgs(reader));
        }

        public void Connect(object Device)
        {
            this._card.Connect((string)Device, SHARE.Shared, PROTOCOL.T0orT1);
            this.Connected = true;
        }

        public void Disconnect()
        {
            try
            {
                //this._card.StopCardEvents();
                this._card.Disconnect(DISCONNECT.Unpower);
                this.Connected = false;
            }
            catch { }     
        }

        public void Dispose()
        {
            if (this.Connected)
                this.Disconnect();

            this._card.StopCardEvents();
        }

        public bool Connected
        {
            private set;
            get;
        }

        public IList Devices
        {
            get
            {
                IList readers_ = this._card.ListReaders();
                if (readers_ != null)
                {
                    foreach (object reader_ in readers_)
                    {
                        this._card.StartCardEvents((string)reader_);
                        //System.Threading.Thread.Sleep(300);
                    }

                    return readers_;
                }
                return new object[] { };
            }
        }

        public byte[] ATR
        {
            get { return this._card.GetAttribute(SCARD_ATTR_VALUE.ATR_STRING); }
        }

        private byte[] _selectFile(byte[] File)
        {
            APDUCommand select = new APDUCommand(0xA0, 0xA4, 0x00, 0x00, File, 0x00);
            APDUResponse response = this._card.Transmit(select);
            
            if (response.SW1 == 0x9F)
            {
                response = this._card.Transmit(new APDUCommand(0xA0, 0xC0, 0x00, 0x00, null, response.SW2));
                return response.SW1 == 0x90 && response.SW2 == 0x00 ? response.Data : null;
            }
            return null;
        }

        public bool PIN1Enabled
        {
            get
            {
                return Convert.ToByte(Convert.ToString(this.MF[13], 2).PadLeft(8, '0')[0].ToString()) == 0 ? true : false;
            }
        }

        public byte[] MF
        {
            get
            {
                return this._selectFile(new byte[] { 0x3F, 0x00 });
            }
        }

        public bool Authenticated
        {
            private set;
            get;
        }

        public int TryPIN1
        {
            get
            {
                if (this.PIN1Enabled)
                {
                    byte try_ = this.MF[18];
                    try_ -= 0x80;

                    return try_;
                }
                return int.MinValue;
            }
        }

        public byte[] PIN1
        {
            set
            {
                APDUCommand chv1 = new APDUCommand(0xA0, 0x20, 0x00, 0x01, value, 0x00);
                APDUResponse response = this._card.Transmit(chv1);
                this.Authenticated = (response.SW1 == 0x90 && response.SW2 == 0x00) ? true : false;
            }
        }

        public GSMAlgorithm RunGSMAlgorithm(byte[] Rand)
        {
            if (this._selectFile(new byte[] { 0x7F, 0x20 }) != null)
            {
                APDUCommand algo_ = new APDUCommand(0xA0, 0x88, 0x00, 0x00, Rand, 0x00);
                APDUResponse response_ = this._card.Transmit(algo_);

                if (response_.SW1 == 0x9F)
                {
                    APDUResponse result_ = this._card.Transmit(new APDUCommand(0xA0, 0xC0, 0x00, 0x00, null, response_.SW2));
                    if (result_.SW1 == 0x90 && result_.SW2 == 0x00)
                    {
                        return new GSMAlgorithm()
                        {
                            SRes = new byte[]
                            {
                                result_.Data[0],
                                result_.Data[1],
                                result_.Data[2],
                                result_.Data[3]
                            },
                            Kc = new byte[] 
                            {
                                result_.Data[4],
                                result_.Data[5],
                                result_.Data[6],
                                result_.Data[7],
                                result_.Data[8],
                                result_.Data[9],
                                result_.Data[10],
                                result_.Data[11]
                            },
                            Rand = Rand
                        };
                    }    
                }
            }

            return null;
        }

        public byte[] IMSI
        {
            get
            {
                if (this._selectFile(new byte[] { 0x7F, 0x20 }) != null)
                {
                    byte[] b_imsi = this._selectFile(new byte[] { 0x6F, 0x07 });
                    if (b_imsi != null)
                    {
                        APDUCommand imsi = new APDUCommand(0xA0, 0xB0, 0x00, 0x00, null, b_imsi[3]);
                        APDUResponse response = this._card.Transmit(imsi);
                        if (response.SW1 == 0x90 && response.SW2 == 0x00)
                        {
                            string s_imsi_ = ByteArray.ToString(response.Data);
                            byte[] b_imsi_ = new byte[(s_imsi_.Length - 2) / 2];
                            
                            for (int i = 2; i < s_imsi_.Length; i = i + 2)
                            {                                
                                b_imsi_[i/2 - 1] = byte.Parse(string.Format("{0:X2}{1:X2}", s_imsi_[i+1], s_imsi_[i]), System.Globalization.NumberStyles.AllowHexSpecifier);
                            }
                            b_imsi_[0] -= 0x80;
                            return b_imsi_;
                        }
                    }
                }
                return null;
            }
        }
    }

    public class Rand
    {
        private Guid _rand;

        public Rand()
        {
            this._rand = Guid.NewGuid();
        }

        public byte[] ToByteArray()
        {
            byte[] b_rand_ = new byte[16];
            string s_rand_ = this.ToString(); 

            for (int i = 0; i < 32; i = i + 2)
                b_rand_[i/2] = byte.Parse(string.Format("{0:X2}{1:X2}", s_rand_[i], s_rand_[i + 1]), System.Globalization.NumberStyles.AllowHexSpecifier);

            return b_rand_;
        }

        public override string ToString()
        {
            return this._rand.ToString().Replace("-", string.Empty);
        }
    }

    public class GSMAlgorithm
    {        
        public byte[] SRes
        {
            internal set;
            get;
        }

        public byte[] Kc
        {
            internal set;
            get;
        }

        public byte[] Rand
        {
            internal set;
            get;
        }
    }

    public class SIMCardInsertedEventArgs : EventArgs
    {
        public string Reader
        {
            private set;
            get;
        }

        public SIMCardInsertedEventArgs(string reader)
        {
            this.Reader = reader;
        }
    }

    public class SIMCardRemovedEventArgs : EventArgs
    {
        public string Reader
        {
            private set;
            get;
        }

        public SIMCardRemovedEventArgs(string reader)
        {
            this.Reader = reader;
        }
    }

    
}
