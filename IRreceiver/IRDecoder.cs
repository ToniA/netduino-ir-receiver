using System;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using System.Threading;

namespace IRDecoder
{
    class IRDecoder
    {
        // Constructor for the IRDecoder class
        public IRDecoder(Cpu.Pin portId, CodeReceivedEventHandler eventHandler)
        {
            // Declare an interrupt port to act on both edges of the input
            this.IRInputPin = new InterruptPort(portId, false, Port.ResistorMode.Disabled, Port.InterruptMode.InterruptEdgeBoth);

            // Declare the interrupt event handler
            this.IRInputPin.OnInterrupt += new NativeEventHandler(RecordPulse);

            // Event handler which fires when a code is received
            this.CodeReceivedHandler += new CodeReceivedEventHandler(eventHandler);

            // Code timeout handler
            this.RCtimeoutTimer = new Timer(new TimerCallback(RCtimeout), null, Timeout.Infinite, Timeout.Infinite);
        }

        // IR input pin
        private InterruptPort IRInputPin;

        // event handler delegate
        public delegate void CodeReceivedEventHandler(String protocol, String data, byte[] bytes);
        
        // Event handler to call when a code is received
        private event CodeReceivedEventHandler CodeReceivedHandler;

        // IR receiver timeout - code is consider received when no state changes in 20 milliseconds
        private const int rc_timeout = 20;
        private Timer RCtimeoutTimer;

        // Raw IR data

        private int position = 0; // Current position in IRDataItems

        private struct IRDataItem
        {
            public Boolean state;
            public long timestamp;
        }

        private IRDataItem[] IRDataItems = new IRDataItem[512];


        // Panasonic E12-CKP protocol, remote control P/N A75C2295
        private const int PANASONIC_AIRCON1_HDR_MARK   = 3400;
        private const int PANASONIC_AIRCON1_HDR_SPACE  = 3500;
        private const int PANASONIC_AIRCON1_BIT_MARK   = 850;
        private const int PANASONIC_AIRCON1_ONE_SPACE  = 2700;
        private const int PANASONIC_AIRCON1_ZERO_SPACE = 950;
        private const int PANASONIC_AIRCON1_MSG_SPACE  = 14000;
        private const int PANASONIC_AIRCON1_SHORT_MSG  = 202;  
        private const int PANASONIC_AIRCON1_LONG_MSG   = 272;
        private const int PANASONIC_AIRCON1_TIMER_MSG  = 420;

        // Panasonic E12-DKE protocol, remote control P/N A75C2616
        private const int PANASONIC_AIRCON2_HDR_MARK   = 3400;
        private const int PANASONIC_AIRCON2_HDR_SPACE  = 1750;
        private const int PANASONIC_AIRCON2_BIT_MARK   = 500;
        private const int PANASONIC_AIRCON2_ONE_SPACE  = 1350;
        private const int PANASONIC_AIRCON2_ZERO_SPACE = 400;
        private const int PANASONIC_AIRCON2_MSG_SPACE  = 10000;
        private const int PANASONIC_AIRCON2_SHORT_MSG  = 264;
        private const int PANASONIC_AIRCON2_LONG_MSG   = 440;

        // Fujitsu Nocria protocol, remote control P/N AR-PZ2
        private const int FUJITSU_AIRCON1_HDR_MARK = 3350;
        private const int FUJITSU_AIRCON1_HDR_SPACE = 1550;
        private const int FUJITSU_AIRCON1_BIT_MARK = 450;
        private const int FUJITSU_AIRCON1_ONE_SPACE = 1150;
        private const int FUJITSU_AIRCON1_ZERO_SPACE = 350;
        private const int FUJITSU_AIRCON1_SHORT_MSG = 116;
        private const int FUJITSU_AIRCON1_LONG_MSG = 260;


        // Pulse recorder, record the pulse timestamp and state
        // and reset the timeout timer
        private void RecordPulse(uint data1, uint data2, DateTime time)
        {
            // Record the timestamp and state
            IRDataItems[position].timestamp = time.Ticks / 10;
            IRDataItems[position].state = (data2 == 1); // pin state as true/false

            // Increment the position
            position++;

            // Start from beginning on overflow
            if (position > IRDataItems.Length)
            {
                position = 0;
            }

            // Reset the timeout timer
            RCtimeoutTimer.Change(rc_timeout, Timeout.Infinite);    // set / reset the timeout timer
        }

        // Timeout fired -> no data in 20 ms -> process the IRDataItems
        private void RCtimeout(object o)
        {
            // Record the final state
            IRDataItems[position].timestamp = DateTime.Now.Ticks / 10;
            IRDataItems[position].state = IRInputPin.Read();

            // Turn off the timeout timer
            RCtimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // Decode the received code
            Decode(position);
            
            // Reset the position and wipe out the buffer
            position = 0;

            for (int j = 1; j < IRDataItems.Length; j++)
            {
                IRDataItems[j].timestamp = 0;
                IRDataItems[j].state = false;
            }
        }

        // Decode the raw received IR code
        private void Decode(int marks)
        {
            String bitString;
            byte[] bytes;
            String protocol;

            if ((bitString = DecodePanasonicAircon1(marks)) != null)
            {
                protocol = "Panasonic CKP";
            }
            else if ((bitString = DecodePanasonicAircon2(marks)) != null)
            {
                protocol = "Panasonic DKE";
            }
            else if ((bitString = DecodeFujitsuAircon1(marks)) != null)
            {
                protocol = "Fujitsu AWYZ";
            }
            else
            {
                protocol = "Unknown, length " + marks;
                bitString = DumpUnknownProtocol(marks);
            }

            // Decode bits of known protocols to a byte array
            if (protocol.Substring(0, "Unknown".Length) != "Unknown")
            {
                bytes = BitstringToBytes(bitString);
            }
            else
            {
                bytes = null;
            }

            // Call the CodeReceivedHandler if defined and we managed to extract something
            if ((CodeReceivedHandler != null) && (bitString != null))
            {
                CodeReceivedHandler(protocol, bitString, bytes);
            }
        }

        // Decode Panasonic E12-CKP code
        // This does not attempt to verify that the code is correct, but
        // it just decodes whatever it gets
        private String DecodePanasonicAircon1(int marks)
        {
            long duration;
            String result = "";

            if ((marks != PANASONIC_AIRCON1_SHORT_MSG) && 
                (marks != PANASONIC_AIRCON1_LONG_MSG) &&
                (marks != PANASONIC_AIRCON1_TIMER_MSG))
                return null;

            for (int j = 1; j < marks; j++)
            {
                duration = IRDataItems[j].timestamp - IRDataItems[j - 1].timestamp;

                if (IRDataItems[j].state == false)
                {
                    if (MatchInterval(duration, PANASONIC_AIRCON1_HDR_SPACE))
                    {
                        result += "H";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON1_ONE_SPACE))
                    {
                        result += "1";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON1_ZERO_SPACE))
                    {
                        result += "0";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON1_MSG_SPACE))
                    {
                        result += "W";
                    }
                    else 
                    {
                        Debug.Print("UNKNOWN SPACE at " + j + " duration " + duration);
                        return null;
                    }
                }
                else
                {
                    if (! (MatchInterval(duration, PANASONIC_AIRCON1_HDR_MARK) || MatchInterval(duration, PANASONIC_AIRCON1_BIT_MARK)))
                    {
                        Debug.Print("UNKNOWN MARK at " + j + " duration " + duration);
                        return null;
                    }
                }
            }

            return result;
        }

        // Decode Panasonic E12-DKE code
        // This does not attempt to verify that the code is correct, but
        // it just decodes whatever it gets
        private String DecodePanasonicAircon2(int marks)
        {
            long duration;
            String result = "";

            if ((marks != PANASONIC_AIRCON2_SHORT_MSG) && (marks != PANASONIC_AIRCON2_LONG_MSG))
                return null;

            for (int j = 1; j < marks; j++)
            {
                duration = IRDataItems[j].timestamp - IRDataItems[j - 1].timestamp;

                if (IRDataItems[j].state == false)
                {
                    if (MatchInterval(duration, PANASONIC_AIRCON2_HDR_SPACE))
                    {
                        result += "H";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON2_ONE_SPACE))
                    {
                        result += "1";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON2_ZERO_SPACE))
                    {
                        result += "0";
                    }
                    else if (MatchInterval(duration, PANASONIC_AIRCON2_MSG_SPACE))
                    {
                        result += "W";
                    }
                    else
                    {
                        //Debug.Print("UNKNOWN SPACE at " + j + " duration " + duration);
                        return null;
                    }
                }
                else
                {
                    if (! (MatchInterval(duration, PANASONIC_AIRCON2_HDR_MARK) || MatchInterval(duration, PANASONIC_AIRCON2_BIT_MARK)))
                    {
                        //Debug.Print("UNKNOWN MARK at " + j + " duration " + duration);
                        return null;
                    }
                }
            }

            return result;
        }

        // Decode Fujitsu AWYZ14 code
        // This does not attempt to verify that the code is correct, but
        // it just decodes whatever it gets
        private String DecodeFujitsuAircon1(int marks)
        {
            long duration;
            String result = "";

            if ((marks != FUJITSU_AIRCON1_SHORT_MSG) && (marks != FUJITSU_AIRCON1_LONG_MSG))
                return null;

            for (int j = 1; j < marks; j++)
            {
                duration = IRDataItems[j].timestamp - IRDataItems[j - 1].timestamp;

                if (IRDataItems[j].state == false)
                {
                    if (MatchInterval(duration, FUJITSU_AIRCON1_HDR_SPACE))
                    {
                        result += "H";
                    }
                    else if (MatchInterval(duration, FUJITSU_AIRCON1_ONE_SPACE))
                    {
                        result += "1";
                    }
                    else if (MatchInterval(duration, FUJITSU_AIRCON1_ZERO_SPACE))
                    {
                        result += "0";
                    }
                    else
                    {
                        //Debug.Print("UNKNOWN SPACE at " + j + " duration " + duration);
                        return null;
                    }
                }
                else
                {
                    if (!(MatchInterval(duration, FUJITSU_AIRCON1_HDR_MARK) || MatchInterval(duration, FUJITSU_AIRCON1_BIT_MARK)))
                    {
                        //Debug.Print("UNKNOWN MARK at " + j + " duration " + duration);
                        return null;
                    }
                }
            }

            return result;
        }

        // Dump unknown protocol as duration of marks and spaces
        private String DumpUnknownProtocol(int marks)
        {
            long duration;
            String result = "";

            for (int j = 1; j < marks; j++)
            {
                duration = IRDataItems[j].timestamp - IRDataItems[j - 1].timestamp;

                if (IRDataItems[j].state == false)
                {
                    result += "s";
                }
                else
                {
                    result += "m";
                }

                result += duration.ToString() + " ";
            }

            return result;
        }

        // Match the mark or space against the actual duration, 20% difference is OK
        private Boolean MatchInterval(long interval, long markduration)
        {
            if ((interval < markduration * 0.8) || (interval > markduration * 1.2))
            {
                return false;
            }

            return true;
        }

        // String of bits to a byte array
        private byte[] BitstringToBytes(String bitString)
        {
            int bits;
            int bitpos = 0;
            int bytepos = 0;
            byte[] bytes;

            // Calculate the number of bits in the string
            bits = (bitString.Split('0').Length - 1) + (bitString.Split('1').Length - 1);

            // .. and the amount of space needed in bytes
            bytes = new byte[(int)System.Math.Ceiling(bits / 8)];

            // Extract the bytes from the bits string
            for (int i = 0; i < bitString.Length; i++)
            {
                if (bitString[i] == '0')
                {
                    bitpos++;
                }
                else if (bitString[i] == '1')
                {
                    bytes[bytepos] += (byte)(1 << bitpos);
                    bitpos++;
                }

                // bit position 8 -> jump to next byte and position 0
                if (bitpos >= 8)
                {
                    bitpos = 0;
                    bytepos++;
                }
            }

            return bytes;
        }
    }
}