using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.NetduinoPlus;
using IRDecoder;

// Program to receive IR codes from an IR receiver module
// Decodes two Panasonic air conditioning infrared protocols
// * E12-CKP (remote control A75C2295)
// * E12-DKE (remote control A75C2616)
//
// Connect the IR receiver module data pin to pin D7
//
// Tested with IR receiver module A742724 from Panasonic E12-CKP indoor unit
//
// The code is based heavily on this example from 'phil': 
// http://forums.netduino.com/index.php?/topic/185-rc6-decoder-class/

namespace IRdemo
{
    public class Program
    {
        public static void Main()
        {
            // Create an instance of the IR decoder
            // listens on pin D7 and calls IRCodeReceived with the decoded message
            IRDecoder.IRDecoder IR = new IRDecoder.IRDecoder(Pins.GPIO_PIN_D7, IRCodeReceived);

            // Main thread just sleeps, everything is handled in events
            Thread.Sleep(Timeout.Infinite);
        }

        // Event handler for the code received event
        static void IRCodeReceived(String protocol, String marks, byte[] bytes)
        {
            String bytestring = "";

            Debug.Print("Received code: " + protocol + "\n" + marks);

            // Decode hex bytes of known protocols
            if (protocol.Substring(0, "Unknown".Length) != "Unknown")
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytestring += bytes[i].ToString("x2");

                    if (i + 1 < bytes.Length)
                    {
                        bytestring += "-";
                    }
                }

                Debug.Print("bytes decoded:\n" + bytestring);

                if (protocol == "Panasonic DKE")
                {
                    PrintPanasonicDKE(bytes);

                    if (CheckDKEHeader(bytes))
                    {
                        Debug.Print("Panasonic DKE header OK");
                    }
                    else
                    {
                        Debug.Print("Panasonic DKE header FAILS");

                    }

                    // Panasonic DKE checksum
                    // 0xF4 plus all bytes but the last == last byte

                    byte checksum = 0xF4;
                    for (int i = 0; i < (bytes.Length - 1); i++)
                    {
                        checksum += bytes[i];
                    }

                    if (checksum == bytes[bytes.Length - 1])
                    {
                        Debug.Print("Panasonic DKE checksum OK");
                    }
                    else
                    {
                        Debug.Print("Panasonic DKE checksum FAILS");
                    }
                }
                else if (protocol == "Panasonic CKP")
                {
                    PrintPanasonicCKP(bytes);
                }
            }
        }

        private static void PrintPanasonicCKP(byte[] bytes)
        {
            // Short message is about setting
            // * ION
            // * QUIET
            // * POWERFUL
            // * SWINGS

            if ((bytes.Length == 12) &&
                CheckRepeated(bytes, 0, 4, 3) &&
                CheckRepeatedPairs(bytes))
            {
                if ((bytes[0] == 0x48) && (bytes[2] == 0x33))
                {
                    Debug.Print("ION ON/OFF");
                }
                else if ((bytes[0] == 0x81) && (bytes[2] == 0x33))
                {
                    Debug.Print("QUIET ON/OFF");
                }
                else if ((bytes[0] == 0x86) && (bytes[2] == 0x35))
                {
                    Debug.Print("POWERFUL ON/OFF");
                }
                else if ((bytes[0] == 0x80) && (bytes[2] == 0x30))
                {
                    Debug.Print("VSWING AUTO");
                }
                else if (((bytes[0] & 0XF0) == 0xA0) && (bytes[2] == 0x32))
                {
                    switch (bytes[0] & 0X0F)
                    {
                        case 0x01:
                            Debug.Print("VSWING FULL UP");
                            break;
                        case 0x02:
                            Debug.Print("VSWING MIDDLE UP");
                            break;
                        case 0x03:
                            Debug.Print("VSWING MIDDLE");
                            break;
                        case 0x04:
                            Debug.Print("VSWING MIDDLE DOWN");
                            break;
                        case 0x05:
                            Debug.Print("VSWING DOWN");
                            break;
                        case 0x08:
                            Debug.Print("HSWING AUTO");
                            break;
                        case 0x09:
                            Debug.Print("HSWING MIDDLE");
                            break;
                        case 0x0C:
                            Debug.Print("HSWING LEFT");
                            break;
                        case 0x0D:
                            Debug.Print("HSWING MIDDLE LEFT");
                            break;
                        case 0x0E:
                            Debug.Print("HSWING MIDDLE RIGHT");
                            break;
                        case 0x0F:
                            Debug.Print("HSWING RIGHT");
                            break;
                        default:
                            Debug.Print("Unknown SWING");
                            break;
                    }
                }
            }

            // Timer message
            else if ((bytes.Length == 24) &&
                      CheckRepeatedPairs(bytes) &&
                      bytes[22] == 0x34)
            {
                if (bytes[0] == 0x7F)
                {
                    Debug.Print("Timer CANCEL message");
                }
                else
                {
                    Debug.Print("Timer SET message");

                    Debug.Print("ON:  " + (bytes[12] - 0x80).ToString("X2") + ":" + bytes[8].ToString("X2"));
                    Debug.Print("OFF: " + (bytes[4]  - 0x80).ToString("X2") + ":" + bytes[0].ToString("X2"));
                }

                Debug.Print("NOW: " +     (bytes[20] - 0x80).ToString("X2") + ":" + bytes[16].ToString("X2"));
            }


            // Everything else is a long message
            else if ((bytes.Length == 16) &&
                        CheckRepeated(bytes, 0, 4, 2) &&
                        CheckRepeated(bytes, 8, 4, 2) &&
                        CheckRepeatedPairs(bytes) &&
                        (bytes[10] == 0x36))
            {
                String Mode = "Unknown";
                String Fan = "AUTO";
                String SwingV;
                String SwingH;

                // Operation mode, low bits of byte 3

                switch (bytes[2] & 0x07)
                {
                    case 0x06:
                        Mode = "AUTO";
                        break;
                    case 0x04:
                        Mode = "HEAT";
                        break;
                    case 0x02:
                        Mode = "COOL";
                        break;
                    case 0x03:
                        Mode = "DRY";
                        break;
                    case 0x01:
                        Mode = "FAN";
                        break;
                }

                if ((bytes[2] & 0x08) != 0x08)
                {
                    Mode += " ON/OFF";
                }

                // Fan speed, high bits of byte 1

                int fan = bytes[0] & 0xF0;
                if (fan != 0xF0)
                {
                    Fan = ((fan >> 4) - 1).ToString();
                }

                // Vertical swing, high bits of byte 9

                switch (bytes[8] & 0XF0)
                {
                    case 0xF0:
                        SwingV = "AUTO";
                        break;
                    case 0xD0:
                        SwingV = "FULL DOWN";
                        break;
                    case 0xC0:
                        SwingV = "MIDDLE DOWN";
                        break;
                    case 0xB0:
                        SwingV = "MIDDLE";
                        break;
                    case 0xA0:
                        SwingV = "MIDDLE UP";
                        break;
                    case 0x90:
                        SwingV = "FULL UP";
                        break;
                    default:
                        SwingV = "Unknown";
                        break;
                }

                // Horizontal swing, low bits of byte 9

                switch (bytes[8] & 0x0F)
                {
                    case 0x08:
                        SwingH = "AUTO";
                        break;
                    case 0x00:
                        SwingH = "MANUAL";
                        break;
                    default:
                        SwingH = "Unknown";
                        break;
                }

                // Print the state

                Debug.Print("MODE   " + Mode);
                Debug.Print("FAN    " + Fan);
                if (Mode != "FAN")
                {
                    Debug.Print("TEMP   " + ((int)(bytes[0] & 0x0F) + 15));
                }
                Debug.Print("SWINGV " + SwingV);
                Debug.Print("SWINGH " + SwingH);
            }
            else
            {
                Debug.Print("protocol error");
            }
        }

        private static void PrintPanasonicDKE(byte[] bytes)
        {
            int timestamp;
            int hours;
            int minutes;
            
            // Short message is about setting
            // * QUIET
            // * POWERFUL

            if (bytes.Length == 16)
            {
                if ((bytes[13] == 0x86) && (bytes[14] == 0x35))
                {
                    Debug.Print("POWERFUL ON/OFF");
                }
                else if ((bytes[13] == 0x81) && (bytes[14] == 0x33))
                {
                    Debug.Print("QUIET ON/OFF");
                }
            }

            // Everything else is a long message
            // Timer messages not yet covered
            else if (bytes.Length == 27)
            {
                String Mode = "Unknown";
                String Fan = "AUTO";
                String SwingV;
                String SwingH;

                // Operation mode, high bits of byte 13

                switch (bytes[13] & 0xF0)
                {
                    case 0x00:
                        Mode = "AUTO";
                        break;
                    case 0x40:
                        Mode = "HEAT";
                        break;
                    case 0x30:
                        Mode = "COOL";
                        break;
                    case 0x20:
                        Mode = "DRY";
                        break;
                    case 0x60:
                        Mode = "FAN";
                        break;
                }

                // ON/OFF, low bit of byte 13

                switch (bytes[13] & 0x01)
                {
                    case 0x00:
                        Mode += " OFF";
                        break;
                    case 0x01:
                        Mode += " ON";
                        break;
                    case 0x0F:
                        Mode += " TIMER ON";
                        break;
                }

                // ION, low bits of byte 22

                switch (bytes[22] & 0x01)
                {
                    case 0x00:
                        Mode += "";
                        break;
                    case 0x01:
                        Mode += " ION";
                        break;
                }

                // Fan speed, high bits of byte 16

                int fan = bytes[16] & 0xF0;
                if (fan != 0xA0)
                {
                    Fan = ((fan >> 4) - 2).ToString();
                }

                // Vertical swing, low bits of byte 16

                switch (bytes[16] & 0X0F)
                {
                    case 0x0F:
                        SwingV = "AUTO";
                        break;
                    case 0x05:
                        SwingV = "FULL DOWN";
                        break;
                    case 0x04:
                        SwingV = "MIDDLE DOWN";
                        break;
                    case 0x03:
                        SwingV = "MIDDLE";
                        break;
                    case 0x02:
                        SwingV = "MIDDLE UP";
                        break;
                    case 0x01:
                        SwingV = "FULL UP";
                        break;
                    default:
                        SwingV = "Unknown";
                        break;
                }

                // Horizontal swing, low bits of byte 17

                switch (bytes[17] & 0x0F)
                {
                    case 0x06:
                        SwingH = "MIDDLE";
                        break;
                    case 0x09:
                        SwingH = "LEFT";
                        break;
                    case 0x0A:
                        SwingH = "LEFT MIDDLE";
                        break;
                    case 0x0B:
                        SwingH = "RIGHT MIDDLE";
                        break;
                    case 0x0C:
                        SwingH = "RIGHT";
                        break;
                    case 0x0D:
                        SwingH = "AUTO";
                        break;
                    default:
                        SwingH = "Unknown";
                        break;
                }

                // Print the state

                Debug.Print("MODE   " + Mode);
                Debug.Print("FAN    " + Fan);
                if ((bytes[13] & 0xF0) != 0x60)
                {
                    Debug.Print("TEMP   " + ((int)(bytes[14]) / 2));
                }
                Debug.Print("SWINGV " + SwingV);
                Debug.Print("SWINGH " + SwingH);


                // Timer OFF

                if (((bytes[13] & 0x0F) != 0x0F) && bytes[19] != 0xE0 && bytes[20] != 0xE0)
                {
                    Debug.Print("Timer CANCEL message");
                }

                // Timer ON

                if ((bytes[13] & 0x0F) == 0x0F)
                {
                    Debug.Print("Timer SET message");

                    // Time ON

                    timestamp = bytes[18] + (int)(bytes[19] & 0x07) * 0x100;
                    hours = (int)System.Math.Floor(timestamp / 60);
                    minutes = timestamp - hours * 60;

                    Debug.Print("ON:  " + hours + ":" + minutes);

                    // Time OFF

                    timestamp = (bytes[19] & 0xF0) / 0x10 + (bytes[20] & 0x0F) * 0x10 + (int)(bytes[20] & 0x70) * 0x10;
                    hours = (int)System.Math.Floor(timestamp / 60);
                    minutes = timestamp - hours * 60;

                    Debug.Print("OFF: " + hours + ":" + minutes);

                    // Time now

                    timestamp = bytes[24] + (int)bytes[25] * 0x100;
                    hours = (int)System.Math.Floor(timestamp / 60);
                    minutes = timestamp - hours * 60;

                    Debug.Print("NOW: " + hours + ":" + minutes);
                }
            }
            else
            {
                Debug.Print("protocol error");
            }
        }


        // Check for repeated patterns in the byte array
        static Boolean CheckRepeated(byte[] bytes, int startingPosition, int offset, int repeats)
        {
            for (int i = 0; i < repeats; i++)
            {
                for (int j = startingPosition; j < offset; j++)
                {
                    if (bytes[j] != bytes[j + i * offset])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Check for repeated pairs in the byte array
        static Boolean CheckRepeatedPairs(byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                if (bytes[i] != bytes[i + 1])
                {
                    return false;
                }
            }

            return true;
        }

        // Check for DKE header
        static Boolean CheckDKEHeader(byte[] bytes)
        {
            if ((bytes[0] != 0x02 ||
                 bytes[1] != 0x20 ||
                 bytes[2] != 0xE0 ||
                 bytes[3] != 0x04 ||
                 bytes[4] != 0x00 ||
                 bytes[5] != 0x00 ||
                 bytes[6] != 0x00 ||
                 bytes[7] != 0x06 ||
                 bytes[8] != 0x02 ||
                 bytes[9] != 0x20 ||
                 bytes[10] != 0xE0 ||
                 bytes[11] != 0x04) &&
               (bytes.Length == 16 &&
                (bytes[12] != 0x80)) &&
               (bytes.Length == 27 &&
                (bytes[18] != 0x00 ||
                 bytes[19] != 0x0E ||
                 bytes[20] != 0xE0 ||
                 bytes[21] != 0x00 ||
                 bytes[23] != 0x01 ||
                 bytes[24] != 0x00 ||
                 bytes[25] != 0x06)
               ))
            {
                return false;
            }

            return true;
        }
    }
}
