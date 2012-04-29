//
// Encog(tm) Framework Indicator - Ninjatrader 7 Version
// Version Beta 1
// http://www.heatonresearch.com/encog/
//
// Copyright 2012 Heaton Research, Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//   
// For more information on Heaton Research copyrights, licenses 
// and trademarks visit:
// http://www.heatonresearch.com/copyright
//
#region Using declarations
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Xml.Serialization;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    /// <summary>
    /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework. What this indicator actually does is totally determined by the Encog-based program it is connected to via sockets.  For more information visit: http://www.heatonresearch.com/wiki/Encog_Framework_Indicator
    /// </summary>
    [Description("(Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).")]
    public class EncogFrameworkIndicator : Indicator
    {
        #region Variables
        // Wizard generated variables
            private string indicatorName = @"default"; // Default setting for Name
            private string host = @"localhost"; // Default setting for Host
            private int port = 1; // Default setting for Port
        // User defined variables (add any user defined variables below)
		public const int TIMEOUT = 20000;	
		
		private enum IndicatorState 
		{
			Uninitialized,
			Ready,
			SentBar,
			Error
		}
					
		private Socket sock;
		private IList<string> sourceData;
		private bool blockingMode = false;
		private string lastPacket;
		private IndicatorState _indicatorState = IndicatorState.Uninitialized;
		private double[] _indicatorData = new double[3];

        #endregion
		
		#region Socket
		protected void Send(String str)
        {
            byte[] msg = Encoding.ASCII.GetBytes(str+"\n");
            sock.Send(msg);
        }
		
		protected void OpenConnection()
		{
			try
            {
                IPAddress[] ips = Dns.GetHostAddresses(host);
                IPAddress targetIP = null;
				
				foreach( IPAddress ip in ips )
				{
					if( ip.AddressFamily == AddressFamily.InterNetwork )
					{
						targetIP = ip;
						break;
					}
				}
				
				if( targetIP!=null )
				{
					IPEndPoint endPoint = new IPEndPoint(targetIP,port);
	                sock = new Socket(targetIP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    	            sock.Connect(endPoint);
        	        Send("\"HELLO\",\"NinjaTrader 7\",\""+indicatorName+"\"");
				}
				else
				{
					Log("Failed to resolve: " + host,LogLevel.Error);
					_indicatorState = IndicatorState.Error;
					return;
				}
				
				sock.ReceiveTimeout = TIMEOUT;
				
				while( _indicatorState != IndicatorState.Ready && _indicatorState != IndicatorState.Error )
				{
					WaitForPacket();
				}
								
				//Log("Timed out waiting for signals packet",LogLevel.Error);								
            }
            catch (Exception ex)
            {
				Log("Encog: Exception: " + ex.ToString(), LogLevel.Error);
            }
		}
		#endregion
		
		#region Parse
		protected IList<string> ParseCSV(string line)
        {
            bool quote = false;
            var temp = new StringBuilder();
            var result = new List<string>();

            foreach(char ch in line)
            {
                if( ch=='\"' )
                {
                    quote = !quote;
                }
                else if( ch==',' && !quote )
                {
                    result.Add(temp.ToString());
                    temp.Length = 0;
                    quote = false;
                }
                else
                {
                    temp.Append(ch);
                }
            }

            result.Add(temp.ToString());
            return result;
        }
		
		protected IList<string> ParseParams(string str)
		{
			var current = new StringBuilder();
			var result = new List<string>();
			bool quote = false;
			
			foreach(char ch in str)
			{
				if( ch==',' && !quote )
				{
					result.Add(current.ToString().Trim());
					current.Length = 0;
				}
				else if( ch=='\"' )
				{
					quote = !quote;
				}
				else 
				{
					current.Append(ch);
				}
			}
			
			if( current.ToString().Trim().Length>0 )
			{
				result.Add(current.ToString().Trim());
			}
			
			return result;
		}
		
		private void ParseArraySize(string str, ref int index, ref string name)
		{
			int idx = str.IndexOf('[');
					
			if( idx==-1 )
			{
				return;
			}
						
			int idx2 = str.IndexOf(']',idx);
						
			if( idx2==-1 )
			{
				return;
			}
			
			string s = str.Substring(idx+1,idx2-idx-1);
			index = int.Parse(s);
			name = str.Substring(0,idx).Trim();
		}
				
		#endregion
		
		#region Packets
		public void WaitForPacket() {
			StringBuilder line = new StringBuilder();			
			byte[] buffer = new byte[1024];
			char[] charBuffer = new char[1024];						
			int actualSize = 0;
				
			try
			{
				actualSize = this.sock.Receive(buffer);
			}
			catch(SocketException ex)
			{
				actualSize = 0;
				if( ex.ErrorCode !=10060 && ex.ErrorCode!=10035 ) 
				{
					Log("Socket Error: " + ex.ToString(), LogLevel.Error);											
				}
			}
				
			if( actualSize>0 ) {					
				ASCIIEncoding ascii = new ASCIIEncoding();
				ascii.GetChars(buffer,0,actualSize,charBuffer,0);
					
				for(int i=0;i<actualSize;i++) 
				{
					char ch = (char)charBuffer[i];
						
					if( ch!='\n' && ch!='\r' ) 
					{
						line.Append(ch);
					} 
					else 
					{
						if( line.Length>0 ) 
						{
							GotPacket(line.ToString());
								line.Length = 0;
						} 
					}
				}
			}
		}
		
		protected void PerformGoodBye()
		{
			try
			{
				Log("Shutting down socket", LogLevel.Information);
				Send("\"GOODBYE\"");			
				sock.Close();
				sock = null;
			}
			finally
			{
				_indicatorState = IndicatorState.Uninitialized;
			}
		}
		
		protected void GotPacket(String line) {			
			Log("Encog: Input packet: " + line + ",len=" + line.Length,LogLevel.Information );
			IList<string> list = ParseCSV(line);
			IList<string> temp = new List<string>();
			
			if( string.Compare(list[0],"signals",true)==0 )
			{				
				sourceData = new List<string>();
				for(int i=1;i<list.Count;i++)
				{
					sourceData.Add(list[i]);
				}

			}
			else if ( string.Compare(list[0],"init",true)==0 )
			{
				blockingMode = list[1].Trim()=="1";
				_indicatorState = IndicatorState.Ready;
				Log("Encog: Blocking mode: " + blockingMode, LogLevel.Information );				
			}
			else if ( string.Compare(list[0],"error",true)==0 )
			{
				Log("Encog Error: " + list[1], LogLevel.Error);
				_indicatorState = IndicatorState.Error;
			}
			else if ( string.Compare(list[0],"warning",true)==0 )
			{
				Log("Encog Warning: " + list[1],LogLevel.Warning);
			}
			else if ( string.Compare(list[0],"ind",true)==0 )
			{
				for(int i=0;i<3;i++)
				{
					_indicatorData[i] = double.Parse(list[i+1]);
				}
				_indicatorState = IndicatorState.Ready;
			}
				
		}
				
		#endregion
		
		#region Evaluate
		protected object[] FillParams(MethodInfo targetMethod, IList<string> paramList)
		{
			ParameterInfo[] pi = targetMethod.GetParameters();
			object[] result = new Object[paramList.Count];
			
			for(int i=0;i<paramList.Count;i++)
			{
				string v = paramList[i];
				Type t = pi[i].ParameterType;
				if( t == typeof(string) )
				{
					result[i] = v;
				}
				else if(  t == typeof(int) || t==typeof(long) )
				{
					result[i] = int.Parse(v);
				}
				else 
				{
					result[i] = double.Parse(v);
				}
			}
			
			
			return result;
		}
		
		protected IDataSeries Eval(string str)
		{
			IList<string> indicatorParams = new List<string>();			
			int index2;
			
			try
			{				
				// first extract the indicator name
				int index = str.IndexOf('(');
				if( index == -1 )
				{
					index = str.Length;	
				}
			
				string indicatorName = str.Substring(0,index).Trim();
			
				// now extract params
				index = str.IndexOf('(',index);

				if( index!=-1 )
				{
					index2 = str.IndexOf(')');
					if( index2!=-1) 
					{
						string s = str.Substring(index+1,index2-index-1).Trim();
						indicatorParams = ParseParams(s);
					}
					else 
					{
						// error
					}
				
					index = index2+1;				
				}
			
				// find the indicator method
				MethodInfo[] methods = GetType().GetMethods();
				MethodInfo targetMethod = null;
			
				foreach( MethodInfo m in methods )
				{
					if( m.Name.CompareTo(indicatorName)==0 && m.GetParameters().Length==indicatorParams.Count)
					{
						targetMethod = m;
					}
				}
			
				// determine if there is a property name
				string propertyName;
				int p = str.IndexOf('.',index);
				if( p!=-1 )
				{
					index = p+1;
					index2 = str.IndexOf('[',index);

					if( index2==-1 )
					{
						propertyName = str.Substring(index);
					}
					else 
					{
						propertyName = str.Substring(index,index2-index);
					}
					index = index2;
				}
				else
				{
					propertyName = "Values";
				}
							
				propertyName = propertyName.Trim();
		
				// execute indicator
				var rawParams = FillParams(targetMethod,indicatorParams);						
				object rtn = targetMethod.Invoke(this,rawParams);
				PropertyInfo pi = rtn.GetType().GetProperty(propertyName);
				IDataSeries ds = (IDataSeries)pi.GetValue(rtn,null);
				
				return ds;				
			}
			catch(Exception ex)
			{
				Log("Eval Error: " + ex.InnerException.ToString(),LogLevel.Error);
				return null;
			}
		}		
		
		#endregion

		
        /// <summary>
        /// This method is used to configure the indicator and is called once before any bar data is loaded.
        /// </summary>
        protected override void Initialize()
        {
            Add(new Plot(Color.FromKnownColor(KnownColor.Orange), PlotStyle.Line, "Plot1"));
            Add(new Plot(Color.FromKnownColor(KnownColor.Red), PlotStyle.Line, "Plot2"));
			Add(new Plot(Color.FromKnownColor(KnownColor.Green), PlotStyle.Line, "Plot3"));
			
			Add(new Plot(Color.FromKnownColor(KnownColor.Orange), PlotStyle.Line, "Bar1"));
            Add(new Plot(Color.FromKnownColor(KnownColor.Red), PlotStyle.Line, "Bar2"));
			Add(new Plot(Color.FromKnownColor(KnownColor.Green), PlotStyle.Line, "Bar3"));
			
            Add(new Plot(Color.FromKnownColor(KnownColor.Firebrick), PlotStyle.TriangleDown, "IndSell"));
            Add(new Plot(Color.FromKnownColor(KnownColor.Green), PlotStyle.TriangleUp, "IndBuy"));
			
            Add(new Line(Color.FromKnownColor(KnownColor.DarkOliveGreen), 0, "Osc1"));
			Add(new Line(Color.FromKnownColor(KnownColor.Khaki), 0, "Osc2"));
			Add(new Line(Color.FromKnownColor(KnownColor.CadetBlue), 0, "Osc3"));
            Overlay				= false;
        }

        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {	
			if( _indicatorState == IndicatorState.Error )
			{
				return;
			}
			
			if( _indicatorState == IndicatorState.Uninitialized )
			{				
				OpenConnection();
			}
			
			if( sock!=null )
			{
				StringBuilder line = new StringBuilder();

				long when = Time[0].Second + 
					Time[0].Minute * 100l +
					Time[0].Hour *   10000l + 
					Time[0].Day *    1000000l +
					Time[0].Month *  100000000l +
					Time[0].Year *   10000000000l;
				
				line.Append("\"BAR\",");				
				line.Append(when);
				line.Append(",\"");
				line.Append(this.Instrument.FullName);
				line.Append("\"");
				
				foreach(string name in this.sourceData)
				{
					IDataSeries source;
					int totalBars = 1;
					string name2 = name;
										
					ParseArraySize(name,ref totalBars, ref name2);
										
					if( string.Compare(name2,"HIGH", true) == 0 )
					{
						source = High;
					}
					else if( string.Compare(name2,"LOW", true) == 0 )
					{
						source = Low;
					}
					else if( string.Compare(name2,"OPEN", true) == 0 )
					{
						source = Open;
					}
					else if( string.Compare(name2,"CLOSE", true) == 0 )
					{
						source = Close;
					}
					else if( string.Compare(name2,"VOL", true) == 0 )
					{
						source = Volume;
					}					
					else 
					{
						source = Eval(name2);
					}
					
					// now copy needed data
					int cnt = CurrentBar + 1;
					
					for(int i=0;i<totalBars;i++) 
					{						
						line.Append(",");

						if( i>=cnt )
						{
							//MessageBox.Show(i + ":?");
							line.Append("?");
						}
						else 
						{	
							//MessageBox.Show(i + ":" + source[i]);
							line.Append(source[i]);
						}
					}
				}
				
				Send(line.ToString());
				
				if( blockingMode )
				{
					_indicatorState = IndicatorState.SentBar;
					while( _indicatorState != IndicatorState.Error && _indicatorState!=IndicatorState.Ready )
					{
						WaitForPacket();
					}
					
					if( _indicatorState == IndicatorState.Ready )
					{
						Plot1.Set(_indicatorData[0]);
            			Plot2.Set(_indicatorData[1]);
					}
				}				
			}			
        }	

		/// <summary>
        /// Called when indicator terminates.
        /// </summary>
		protected override void OnTermination() 
		{
			if(sock!=null) 
			{				
				Log("OnTermination called, shutting down connection.",LogLevel.Information);
				PerformGoodBye();
			}
		}

        #region Properties
        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Plot1
        {
            get { return Values[0]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Plot2
        {
            get { return Values[1]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Plot3
        {
            get { return Values[2]; }
        }
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Bar1
        {
            get { return Values[3]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Bar2
        {
            get { return Values[4]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries Bar3
        {
            get { return Values[5]; }
        }

        [Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries IndSell
        {
            get { return Values[6]; }
        }
		
		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public DataSeries IndBuy
        {
            get { return Values[7]; }
        }

        [Description("Remote Indicator Name")]
        [GridCategory("Parameters")]
        public string IndicatorName
        {
            get { return indicatorName; }
            set { indicatorName = value; }
        }

        [Description("Remote Host")]
        [GridCategory("Parameters")]
        public string Host
        {
            get { return host; }
            set { host = value; }
        }

        [Description("Remote Port")]
        [GridCategory("Parameters")]
        public int Port
        {
            get { return port; }
            set { port = Math.Max(1, value); }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.
// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.Indicator
{
    public partial class Indicator : IndicatorBase
    {
        private EncogFrameworkIndicator[] cacheEncogFrameworkIndicator = null;

        private static EncogFrameworkIndicator checkEncogFrameworkIndicator = new EncogFrameworkIndicator();

        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        public EncogFrameworkIndicator EncogFrameworkIndicator(string host, string indicatorName, int port)
        {
            return EncogFrameworkIndicator(Input, host, indicatorName, port);
        }

        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        public EncogFrameworkIndicator EncogFrameworkIndicator(Data.IDataSeries input, string host, string indicatorName, int port)
        {
            if (cacheEncogFrameworkIndicator != null)
                for (int idx = 0; idx < cacheEncogFrameworkIndicator.Length; idx++)
                    if (cacheEncogFrameworkIndicator[idx].Host == host && cacheEncogFrameworkIndicator[idx].IndicatorName == indicatorName && cacheEncogFrameworkIndicator[idx].Port == port && cacheEncogFrameworkIndicator[idx].EqualsInput(input))
                        return cacheEncogFrameworkIndicator[idx];

            lock (checkEncogFrameworkIndicator)
            {
                checkEncogFrameworkIndicator.Host = host;
                host = checkEncogFrameworkIndicator.Host;
                checkEncogFrameworkIndicator.IndicatorName = indicatorName;
                indicatorName = checkEncogFrameworkIndicator.IndicatorName;
                checkEncogFrameworkIndicator.Port = port;
                port = checkEncogFrameworkIndicator.Port;

                if (cacheEncogFrameworkIndicator != null)
                    for (int idx = 0; idx < cacheEncogFrameworkIndicator.Length; idx++)
                        if (cacheEncogFrameworkIndicator[idx].Host == host && cacheEncogFrameworkIndicator[idx].IndicatorName == indicatorName && cacheEncogFrameworkIndicator[idx].Port == port && cacheEncogFrameworkIndicator[idx].EqualsInput(input))
                            return cacheEncogFrameworkIndicator[idx];

                EncogFrameworkIndicator indicator = new EncogFrameworkIndicator();
                indicator.BarsRequired = BarsRequired;
                indicator.CalculateOnBarClose = CalculateOnBarClose;
#if NT7
                indicator.ForceMaximumBarsLookBack256 = ForceMaximumBarsLookBack256;
                indicator.MaximumBarsLookBack = MaximumBarsLookBack;
#endif
                indicator.Input = input;
                indicator.Host = host;
                indicator.IndicatorName = indicatorName;
                indicator.Port = port;
                Indicators.Add(indicator);
                indicator.SetUp();

                EncogFrameworkIndicator[] tmp = new EncogFrameworkIndicator[cacheEncogFrameworkIndicator == null ? 1 : cacheEncogFrameworkIndicator.Length + 1];
                if (cacheEncogFrameworkIndicator != null)
                    cacheEncogFrameworkIndicator.CopyTo(tmp, 0);
                tmp[tmp.Length - 1] = indicator;
                cacheEncogFrameworkIndicator = tmp;
                return indicator;
            }
        }
    }
}

// This namespace holds all market analyzer column definitions and is required. Do not change it.
namespace NinjaTrader.MarketAnalyzer
{
    public partial class Column : ColumnBase
    {
        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.EncogFrameworkIndicator EncogFrameworkIndicator(string host, string indicatorName, int port)
        {
            return _indicator.EncogFrameworkIndicator(Input, host, indicatorName, port);
        }

        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        public Indicator.EncogFrameworkIndicator EncogFrameworkIndicator(Data.IDataSeries input, string host, string indicatorName, int port)
        {
            return _indicator.EncogFrameworkIndicator(input, host, indicatorName, port);
        }
    }
}

// This namespace holds all strategies and is required. Do not change it.
namespace NinjaTrader.Strategy
{
    public partial class Strategy : StrategyBase
    {
        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        [Gui.Design.WizardCondition("Indicator")]
        public Indicator.EncogFrameworkIndicator EncogFrameworkIndicator(string host, string indicatorName, int port)
        {
            return _indicator.EncogFrameworkIndicator(Input, host, indicatorName, port);
        }

        /// <summary>
        /// (Version Beta-1) This is a generic indicator used to connect to the Encog Framework(www.encog.org).
        /// </summary>
        /// <returns></returns>
        public Indicator.EncogFrameworkIndicator EncogFrameworkIndicator(Data.IDataSeries input, string host, string indicatorName, int port)
        {
            if (InInitialize && input == null)
                throw new ArgumentException("You only can access an indicator with the default input/bar series from within the 'Initialize()' method");

            return _indicator.EncogFrameworkIndicator(input, host, indicatorName, port);
        }
    }
}
#endregion
