netduino-ir-receiver
====================

Infrared receiver for Netduino, decodes Panasonic air conditioner IR commands.

Decodes these Panasonic air conditioning infrared protocols:
* E12-CKP (remote control A75C2295)
* E12-DKE (remote control A75C2616)

And this Fujitsu (just the bytes so far):
* Nocria AWYZ14 (remote control P/N AR-PZ2)

Connect the IR receiver module data pin to pin D7, ground to GND and Vcc to 5V on Netduino.

Tested with IR receiver module A742724, taken from Panasonic E12-CKP indoor unit.

The code is based heavily on this example from 'phil': 
http://forums.netduino.com/index.php?/topic/185-rc6-decoder-class/
