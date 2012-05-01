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
using System.Windows.Forms;
using System.Globalization;
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
            private int port = 5128; // Default setting for Port
        // User defined variables (add any user defined variables below)
		public const int TIMEOUT = 20000;	
		
		/// <summary>
		/// The indicator is stateful, and can be in the following states.
		/// </summary>
		private enum IndicatorState 
		{
			/// <summary>
			/// The indicator has not yet connected.
			/// </summary>
			Uninitialized,
			/// <summary>
			/// The indicator has connected and is ready for use.
			/// Also indicates that the indicator is not blocking
			/// and waiting for a bar.
			/// </summary>
			Ready,
			/// <summary>
			/// The indicator has sent a bar, and is now waiting on
			/// a response from Encog.
			/// </summary>
			SentBar,
			/// <summary>
			/// An error has occured.  The indicator is useless at 
			/// this point and will perform no further action.
			/// </summary>
			Error
		}
		
		/// <summary>
        /// The socket that we are using to communicate.
        /// </summary>
		private Socket _sock;
		
		/// <summary>
		/// The source data requested, for example HIGH, LOW, etc, as well as 
		/// 3rd party indicators.
		/// </summary>
		private IList<string> _sourceData;
		
		/// <summary>
		/// Are we in blocking mode?  If we are just downloading data, then probably not
		/// in blocking mode.  If we are going to actually display indicator data then we
		/// are in blocking mode.  Blocking mode requres a "bar" response from Encog for
		/// each bar.  Non-blocking mode can simply "stream".
		/// </summary>
		private bool _blockingMode = false;
		
		/// <summary>
		/// The state of the indicator.  
		/// </summary>
		private IndicatorState _indicatorState = IndicatorState.Uninitialized;
				
		/// <summary>
		/// The output data from Encog to diplay as the indicator.
		/// </summary>		
		private double[] _indicatorData = new double[8];
		
		/// <summary>
		/// Error text that should be displayed if we are in an error state.
		/// </summary>
		private string _errorText;
		
		/// <summary>
		/// Do not change this.  The "wire protocol" uses USA format numbers.
		/// This does not affect display.
		/// </summary>
		private readonly CultureInfo _cultureUSA = new CultureInfo("en-US");

        #endregion
		
		#region Socket
		
		/// <summary>
		/// Send data to the remote socket, if we are connected.
		/// If we are not connected, ignore. Data is sent in ASCII.
		/// </summary>
		/// <param name="str">The data to send to the remote.</param>
		protected void Send(String str)
        {
			if( _sock!=null )
			{
            	byte[] msg = Encoding.ASCII.GetBytes(str+"\n");
            	_sock.Send(msg);
			}
        }
		
		/// <summary>
		/// Open a connection to Encog. Also send the HELLO packet.
		/// </summary>
		protected void OpenConnection()
		{
			try
            {
                IPAddress[] ips = Dns.GetHostAddresses(host);
                IPAddress targetIP = null;
				
				// first we need to resolve the host name to an IP address
				foreach( IPAddress ip in ips )
				{
					if( ip.AddressFamily == AddressFamily.InterNetwork )
					{
						targetIP = ip;
						break;
					}
				}
				
				// if successful, then connect to the remote and send the HELLO packet.
				if( targetIP!=null )
				{
					IPEndPoint endPoint = new IPEndPoint(targetIP,port);
	                _sock = new Socket(targetIP.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    	            _sock.Connect(endPoint);
        	        Send("\"HELLO\",\"NinjaTrader 7\",\""+indicatorName+"\"");
				}
				else
				{
					// report failure and exit
					PerformError("Failed to resolve: " + host);
					return;
				}
				
				// make sure the socket will timeout
				_sock.ReceiveTimeout = TIMEOUT;
				_indicatorState = IndicatorState.Uninitialized;
				
				while( _indicatorState == IndicatorState.Uninitialized )
				{
					WaitForPacket();
				}							
            }
            catch (Exception ex)
            {
				_sock = null;
				PerformError(ex.Message);
            }
		}
		#endregion
		
		#region Parse
		
		/// <summary>
		/// Parse a line of CSV.  Lines can have quote escaped text.
		/// A quote inside a string should be HTML escaped (i.e. &quot;)
		/// Only &quot; is supported, and it must be lower case.
		/// 
		/// Ideally, I would like to use HttpUtility, however, in .Net
		/// 3.5, used by NT, this requires an additonal reference, 
		/// which is an extra step in the setup process.
		/// </summary>
		/// <param name="line">The line to parse.</param>
		/// <returns>A list of values.</returns>
		protected IList<string> ParseCSV(string line)
        {					
            var quote = false;
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
                    result.Add(temp.ToString().Replace("&quot;","\""));
                    temp.Length = 0;
                    quote = false;
                }
                else
                {
                    temp.Append(ch);
                }
            }

            result.Add(temp.ToString().Replace("&quot;","\""));
            return result;
        }
		
		/// <summary>
		/// Parse an array size, for example "avg[5]".  Return both the
		/// number, and the name before the brakets.
		/// </summary>
		/// <param name="str">The string to parse.</param>
		/// <param name="index">The index parsed.</param>
		/// <param name="name">The name parsed.</param>
		private void ParseArraySize(string str, ref int index, ref string name)
		{
			var idx = str.IndexOf('[');
					
			if( idx==-1 )
			{
				return;
			}
						
			var idx2 = str.IndexOf(']',idx);
						
			if( idx2==-1 )
			{
				return;
			}
			
			var s = str.Substring(idx+1,idx2-idx-1);
			index = int.Parse(s);
			
			if( index<1 ) 
			{
				index = 1;
			}
			
			name = str.Substring(0,idx).Trim();
		}
				
		#endregion
		
		#region Packets
		
		/// <summary>
		/// The HELLO packet, sent from the client to the server to provide version information.
		/// </summary>
		public const string PACKET_HELLO = "HELLO";
	
		/// <summary>
		/// The GOODBYE packet, sent from the client to the server to end communication.
		/// </summary>
		public const string PACKET_GOODBYE = "GOODBYE";
	
		/// <summary>
		/// The SIGNALS packet, sent from the client to the server to specify requested data.
		/// </summary>
		public const string PACKET_SIGNALS = "SIGNALS";
	
		/// <summary>
		/// The INIT packet, sent from the server to the client to provide config information.
		/// </summary>
		public const string PACKET_INIT = "INIT";
	
		/// <summary>
		/// The BAR packet, sent from the client to the server at the end of each BAR.
		/// </summary>
		public const string PACKET_BAR = "BAR";
	
		/// <summary>
		/// The IND packet, sent from the server to the clinet, in response to a BAR. 
		/// </summary>
		public const string PACKET_IND = "IND";
		
		/// <summary>
		/// The ERROR packet, used to move to an error state.
		/// </summary>
		public const String PACKET_ERROR = "ERROR";
	
		/// <summary>
		/// The WARNING packet, used to log a warning.
		/// </summary>
		public const String PACKET_WARNING = "WARNING";
		
		/// <summary>
		/// Wait for a packet.  Timeout if necessary.
		/// </summary>
		public void WaitForPacket() {
			var line = new StringBuilder();			
			var buffer = new byte[1024];
			var charBuffer = new char[1024];						
			var actualSize = 0;
			
			// if there was an error, nothing to wait for.
			if( this._indicatorState == IndicatorState.Error )
			{
				return;				
			}
			
			// attempt to get a packet
			try
			{
				actualSize = _sock.Receive(buffer);
			}
			catch(SocketException ex)
			{
				PerformError("Socket Error: " + ex.Message);
				return;
			}
				
			// If we got a packet, then process it.
			if( actualSize>0 ) {					
				// packets are in ASCII
				ASCIIEncoding ascii = new ASCIIEncoding();
				ascii.GetChars(buffer,0,actualSize,charBuffer,0);
					
				// Break up the packets, they are ASCII lines.
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
		
		/// <summary>
		/// Handle an error.  Display the error to the user, log it,
		/// and set the state to error.  No further processing on this
		/// indicator will occur.
		/// </summary>
		/// <param name="whatError">The error text.</param>
		protected void PerformError(string whatError)
		{
			try
			{
				_indicatorState = IndicatorState.Error;
				_errorText = whatError;
				Log("Encog Error: " + whatError ,LogLevel.Error);
				Log("Encog: Shutting down socket", LogLevel.Information);
				Send("\""+PACKET_GOODBYE+"\"");			
				_sock.Close();
				_sock = null;
			}
			finally
			{				
				DrawError();
			}
		}
		
		/// <summary>
		/// Tell the remote that we are about to disconnect.
		/// </summary>
		protected void PerformGoodBye()
		{
			try
			{
				Log("Shutting down socket", LogLevel.Information);
				Send("\""+PACKET_GOODBYE+"\"");			
				_sock.Close();
				_sock = null;
			}
			finally
			{				
				_indicatorState = IndicatorState.Uninitialized;
			}
		}
		
		/// <summary>
		/// Process a packet.
		/// </summary>
		/// <param name="line">The packet line.</param>
		protected void GotPacket(String line) {			
			var list = ParseCSV(line);
			var temp = new List<string>();
			
			list[0] = list[0].ToUpper();
			
			// a SIGNALS packet tells us what indicators and fund data the remote wants.
			if( string.Compare(list[0],PACKET_SIGNALS,true)==0 )
			{				
				_sourceData = new List<string>();
				for(int i=1;i<list.Count;i++)
				{
					_sourceData.Add(list[i]);
				}

			}
			// The INIT packet tells us what protocol version we are using, 
			// and if blocking mode is requested.
			else if ( string.Compare(list[0],PACKET_INIT,true)==0 )
			{
				_blockingMode = list[1].Trim()=="1";
				_indicatorState = IndicatorState.Ready;
				Log("Encog: Blocking mode: " + _blockingMode, LogLevel.Information );				
			}
			// The ERROR packet allows the server to put us into an error state and
			// provide a string that tells the reason.
			else if ( string.Compare(list[0],PACKET_ERROR,true)==0 )
			{
				PerformError("Server Error: " + list[1]);
			}
			// The WARNING packet allows the server to log a warning.
			else if ( string.Compare(list[0],PACKET_WARNING,true)==0 )
			{
				Log("Encog Warning: " + list[1],LogLevel.Warning);
			}
			// The IND packet provides indicator data, to be displayed.  Only used
			// when in blocking mode.
			else if ( string.Compare(list[0],PACKET_IND,true)==0 )
			{
				if( list.Count != 9 )
				{
					PerformError("Not enough indicator values from Encog, must have 8, had:" + (list.Count-1));
					return;
				}
						
				try
				{
					for(int i=0;i<8;i++)
					{
						if( "?".Equals(list[i+1]) )
						{
							_indicatorData[i] = double.NaN;
						}
						else
						{
							//_indicatorData[i] = double.Parse(list[i+1]);
							_indicatorData[i] = double.Parse(list[i+1],_cultureUSA);
						}
					}
				}
				catch(FormatException ex)
				{
					PerformError(ex.Message);
				}
				_indicatorState = IndicatorState.Ready;
			}
				
		}
				
		#endregion
		
		#region Evaluate
		
		/// <summary>
		/// Fill the parameters of a 3rd party indicator.
		/// </summary>
		/// <param name="targetMethod">The name of the indicator( i.e. "MACD(12,26,9)")</param>
		/// <param name="paramList">The parameter list.</param>
		/// <returns>The objects</returns>
		protected object[] FillParams(MethodInfo targetMethod, IList<string> paramList)
		{
			ParameterInfo[] pi = targetMethod.GetParameters();
			object[] result = new Object[paramList.Count];
			
			// loop over the parameters and create objects of the correct type.
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
		
		/// <summary>
		/// Evaluate a custom indicator. These indicators must be in the form:
		/// 
		/// INDICATOR.VALUE[BARS_NEEDED]
		/// 
		/// For example, 
		/// 
		/// MACD(12,26,9).Avg[1]
		/// 
		/// This would request the current BAR of the Avg value of the MACD indicator.
		/// If the indicator has only one value, then the following format is used.
		/// 
		/// EMA(14)[1]
		/// 
		/// This would request the current bar of EMA, with a period of 14.
		/// </summary>
		/// <param name="str">The indicator string.</param>
		/// <returns></returns>
		protected IDataSeries Eval(string str)
		{
			IList<string> indicatorParams = new List<string>();			
			int index2;
			
			try
			{				
				// first extract the indicator name
				var index = str.IndexOf('(');
				if( index == -1 )
				{
					index = str.Length;	
				}
			
				var indicatorName = str.Substring(0,index).Trim();
			
				// now extract params
				index = str.IndexOf('(',index);

				if( index!=-1 )
				{
					index2 = str.IndexOf(')');
					if( index2!=-1) 
					{
						var s = str.Substring(index+1,index2-index-1).Trim();
						indicatorParams = ParseCSV(s);
					}
					else 
					{
						PerformError("Invalid custom indicator: " + str);
						return null;
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
				var p = str.IndexOf('.',index);
				
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
				
				if( rtn==null )
				{
					PerformError("Custom indicator returned null: " + str);
					return null;
				}
				
				var pi = rtn.GetType().GetProperty(propertyName);
				
				if( pi==null )
				{
					PerformError("Custom indicator property not found: " + str);
					return null;
				}
				
				IDataSeries ds = (IDataSeries)pi.GetValue(rtn,null);
				
				return ds;				
			}
			catch(Exception ex)
			{
				PerformError("Eval Error: " + ex.InnerException.ToString());
			}
			
			return null;
		}		
		
		#endregion

		#region GeneralUtil
		void DrawError() 
		{						
			if( this.ChartControl !=null && _errorText !=null )
			{
				bool hold = this.DrawOnPricePanel;				
				this.DrawOnPricePanel = false;
				this.DrawTextFixed("error msg", "ERROR:" + _errorText, TextPosition.Center);
				this.DrawOnPricePanel = hold;
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
			
			Console.WriteLine("Init");
        }

        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {	
			// Try to connect, if we are not yet connected.
			// We do this here so that we do not connect everytime the indicator is instanciated.
			// Indicators are often instanciated several times before they are actually used.
			if( _indicatorState == IndicatorState.Uninitialized )
			{				
				OpenConnection();
			}
			
			// Are we in an error state?  If so, display and exit.
			if( _indicatorState == IndicatorState.Error )
			{				
				DrawError();
				return;
			}
												
			// If we are actually connected to a socket, then communicate with it.
			if( _sock!=null )
			{
				StringBuilder line = new StringBuilder();

				long when = Time[0].Second + 
					Time[0].Minute * 100l +
					Time[0].Hour *   10000l + 
					Time[0].Day *    1000000l +
					Time[0].Month *  100000000l +
					Time[0].Year *   10000000000l;
				
				line.Append("\""+PACKET_BAR+"\",");				
				line.Append(when);
				line.Append(",\"");
				line.Append(this.Instrument.FullName);
				line.Append("\"");
				
				foreach(string name in _sourceData)
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
					else if( string.Compare(name2,"THIS", true) == 0 )
					{
						source = Values[0];
					}
					else 
					{
						source = Eval(name2);
						if( source==null )
						{
							return;
						}
					}
					
					// now copy needed data
					var cnt = CurrentBar + 1;
					
					for(int i=0;i<totalBars;i++) 
					{						
						line.Append(",");

						if( i>=cnt )
						{
							line.Append("?");
						}
						else 
						{								
							//line.Append(Convert.ToString(source[i]));
							line.Append(Convert.ToString(source[i],_cultureUSA));
						}
					}
				}
				
				Send(line.ToString());
				
				// if we are expecting data back from the socket, then wait for it.
				if( _blockingMode )
				{
					// we are now waiting for a bar
					_indicatorState = IndicatorState.SentBar;
					while( _indicatorState != IndicatorState.Error && _indicatorState!=IndicatorState.Ready )
					{
						WaitForPacket();
					}
					
					// we got a bar message, then display it
					if( _indicatorState == IndicatorState.Ready )
					{	
						if( !double.IsNaN(_indicatorData[0]) )
						{
							Plot1.Set(_indicatorData[0]);
						}
						
						if( !double.IsNaN(_indicatorData[1]) )
						{
            				Plot2.Set(_indicatorData[1]);
						}
							
						if( !double.IsNaN(_indicatorData[2]) )
						{
							Plot3.Set(_indicatorData[2]);
						}
							
						if( !double.IsNaN(_indicatorData[3]) )
						{
							Bar1.Set(_indicatorData[3]);
						}
							
						if( !double.IsNaN(_indicatorData[4]) )
						{
            				Bar2.Set(_indicatorData[4]);
						}
							
						if( !double.IsNaN(_indicatorData[5]) )
						{
							Bar3.Set(_indicatorData[5]);
						}
							
						if( !double.IsNaN(_indicatorData[6]) )
						{
							IndSell.Set(_indicatorData[6]);
						}
							
						if( !double.IsNaN(_indicatorData[7]) )
						{	
            				IndBuy.Set(_indicatorData[7]);
						}
					}
				}
				else 
				{
					var hold = this.DrawOnPricePanel;				
					DrawOnPricePanel = false;
					DrawTextFixed("general msg","This indicator only sends data, so there is no display.",TextPosition.Center);
					DrawOnPricePanel = hold;					
				}
			}
        }	

		/// <summary>
        /// Called when indicator terminates.
        /// </summary>
		protected override void OnTermination() 
		{
			if(_sock!=null) 
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
