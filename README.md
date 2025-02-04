# Introduction 
This project attempts to extract highscores from various pinball applications, so they can be exported to a database.

# Getting Started
Build and run. It should work out of the box and give you a list of high scores from all sample data files included in the solution (these are not up to date with the office machine).  
If you have problems, have a look at the paths defined in App.config.  
There could also be issues with the PINemHi config. It does not accept full file paths but insist on using configured paths to the nvram folder. It is set to be relative, but in case it messes up it can be edited in ScoresData/PINemHi/pinemhi.ini.
*This probably only works on Windows! It is a .NET Core app, but the extractors are not.*

# Data Formats
VPinMAME tables save data in NVRAM, located in the vpinmame/nvram folder (sample data included in ScoresData/nvram). Each table has its own .nv file.  
NVRAM files each have a custom binary format with specific memory locations for scores. We can use PINemHi (http://www.pinemhi.com/) to extract from supported tables as command line output, and then parse that.  
  
Visual Pinball X native tables save data in a shared file, User/VPReg.stg (included in ScoresData/User/VPReg.stg).  
This .stg file uses an old Microsoft format, Compound Storage Files, commonly used by Outlook. They can be parsed with C++ ole32.dll funnctions, imported in the MSStorage folder.  
  
Pinball FX3 saves data in an unknown proprietary format. It is *probably* stored in the included ScoresData/FX3/Profile.dat file, but it seems to be encrypted. Further research is needed.

# Tests
A unit test project is included, but barely implemented. Feel free :p