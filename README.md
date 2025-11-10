# VoteBKM-MenuManager
Modified version of VoteBKM cssharp plugin by  ebpnk/DoctorishHD to work with MenuManager plugin by NickFox007

Description

I do not claim any ownership or any credits to this plugin

All credits for creating the original plugin go to https://github.com/ebpnk/ (https://github.com/ebpnk/VoteBKM)

Also this wasn't really tested in an actual server environment.

It's basically the same plugin but was edited slightly to work with MenuManager plugin by https://github.com/NickFox007/ (https://github.com/NickFox007/MenuManagerCS2)
(The menu type that plugin uses now utilizes the one used by default in MenuManager plugin)

I'm not a programmer just changed some lines based on this example https://github.com/NickFox007/MenuManagerCS2/tree/main/MenuManagerTest
So bare this in mind if you encounter any issues. Guess I'll update this ReadMe when I actually use it on my server. The new menu works tho.

Also since I'm not a programmer I don't really know how to make it work with a database and how to make it so that you can translate the plugin in the config file. (Translation is already embedded into the .cs project (You can translate it there and build yourself))
Theoretically all bans should be stored and checked in the generated config file. (No idea how optimal is that approach but it is what it is)

How to use:

Download as zip -> unzip -> choose a translation (en/ru) -> you only need the VoteBKM folder -> put it into ../game/csgo/addons/counterstrikesharp/plugins

Once you restart the server a new config file will be generated upon first loading of the plugin (voteban_config.json) (same folder)

This is what you can edit in the config:

{
  "BanDuration": 3600,// Time in SECONDS
  "RequiredMajority": 0.5,//Percentage of votes,50% - 0.5 (I recommend to set it to at least 0.51)
  "MinimumPlayersToStartVote": 4 // The beginning of voting depends on the number of players (good default value)
}

Restart the server for config to update (I guess?)

That's it. I hope someone finds this useful or maybe a proper programmer can improve it to work with a database and what not.
