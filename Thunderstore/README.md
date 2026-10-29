 # Configurable Trials

## Hallowed Concepts is REQUIRED for this mod
Should be pretty obvious since this mod edits one of the core features for that DLC, but figured to get that disclaimer out there just in-case.

## Features
#### Adjustable trial timer
Trials normally have a rigid 3min 30sec timer, now you're free to adjust to your preference both initially and with ways to earn more time.

#### Earnable time
Time can now be earned by clearing stages, looping, or finishing Soulbound Threshold. Try [this mod](https://thunderstore.io/c/riskofrain2/p/Slendykray/RenewableTrial/) if you want Karma to increase time too.

![Extra time](https://raw.githubusercontent.com/ShandoVK/ConfigurableTrials/refs/heads/main/Images/TimeExtended.png)


#### Custom Starting Items
You can now set items of your choice that you and other players will temporarily have for a trial. This supports many mods that adds items, such as [Sandswept](https://thunderstore.io/c/riskofrain2/p/SandsweptTeam/Sandswept/), [Starstorm 2](https://thunderstore.io/c/riskofrain2/p/TeamMoonstorm/Starstorm2/), [Augmentum](https://thunderstore.io/c/riskofrain2/p/TeamAugmentum/Augmentum/), [Faithful](https://thunderstore.io/c/riskofrain2/p/SpilledSoup_Projects/Faithful/), and many more. (You will need any respective DLCs tied to vanilla DLC items or Void-Tiered items for the option to work)
![IS THAT A SKATEBOARD](https://raw.githubusercontent.com/ShandoVK/ConfigurableTrials/refs/heads/main/Images/Look%20ma%2C%20a%20skateboard!.png)


#
The item selector at the start for trials has also been updated to support modded items.
![More items](https://github.com/ShandoVK/ConfigurableTrials/blob/main/Images/more%20stuff.png)

#### Repeatable Trials
In vanilla, all trials can only be done once in a run (totaling to 9 max trials). This mod allows you to be able to do an infinite number of trials if your heart desires to do so.


#### Trial Filtering
You can now blacklist individual trials, no longer are you potentially forced to yeet enemies off the map or breaking lamps as a melee.


#
All of the features listed above can be configured to your liking, and supports **Risk of Options** as well.

## Known Issues
- Sibylline & Highlander-tiered items seem to not correctly get stored with the rest of the player's inventory when a trial starts. This may result in instances like 2 simultaneous Highlander items held when that previously should be impossible.

- Item drops in a run (especially white-tier ones) are basically only vanilla. None of my methods item changing methods touch item drops (including items from interactables) and each trial seems to have a specific curtailed set of items to spawn. Might explore an option to change the drops if there's a feasible way to dynamically read in the available item pool but no guarantee on that.

- Some trials (e.g. Abyss) seem to be wonky in multiplayer, I believe this might be on Gearbox's end but if any is caused by this mod, do report it on github

## For Developers
My fix just appends CanBeRental to the ItemDef's tags (This is the tag for Ethereal items for DLC4). Applying this tag to the asset's ItemDef should fix the item being missing in the selector.