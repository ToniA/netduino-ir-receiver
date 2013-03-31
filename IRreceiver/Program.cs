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
        static void IRCodeReceived(String data)
        {
            Debug.Print("Received code:\n" + data);
        }
    }
}
