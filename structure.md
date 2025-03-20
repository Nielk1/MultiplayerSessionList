# notes
## data priority
Some data paths override others.
For example session.level.rules data can override session.level.map data can override session.level data.
This allows for logic where the level has set a game mode, but the map data contains more narrowly tailored game mode data, but the user can further override the active game mode should rules selection.
This is because rules and level are session instance specific but map data is likely fixed (as map data objects are often reused).

## Game Notation
At this stage unusual and other fields listed are tagged with their game of origin.
This notation is for the current stage where the spec is still fluid.
Generally reading the API should do so with fallback chains for all properties as some games may set properties and others may not.
Once the spec is more stable I will try to document game specifics globally.


# enum string values
## session_state
* `unknown` // Normally has a state, but it is unknown
* `pre_game` // Pre-Game Setup
* `loading` // Loading Game
* `in_game` // In-Game
* `post_game` // Post-Game or GameOver
* `paused` // unused options so far
* `away` // unused options so far
* `busy` // unused options so far
* `not_responding` // unused options so far

# objects
## default
* *type*: `object`

## session
* type: `string` (enum) `"listen","dedicated"`
* sources:
   * *key*: `object` ([source](#source))
* name: `string`
* message: `string`
* game:
  * mod: `object` ([mod](#mod))
  * mods: `[object]` ([mod](#mod))
  * version: `string` (version format or any?)
  * game_balance: `object` ([game_balance](#game_balance))
  * other: *object* (game specific)
    * mod_hash: `string` [bigboat:battlezone_combat_commander]
* status:
  * is_locked: `bool`
  * has_password: `bool`
  * has_password.spectator: `bool` [retroarch:netplay] **likely needs adjustment**
  * state: `string` (enum)
  * other: *object* (game specific)
    * sync_too_late: `bool` [bigboat:battlezone_98_redux] (too late to join due to sync_join bug)
* address: **work needed here**
  * token: `string`
  * other: *object* (game specific)
    * lobby_id: `integer` [bigboat:battlezone_98_redux]
    * nat: `string` [bigboat:battlezone_combat_commander]
    * IP: `string` [retroarch:netplay]
    * Port: `integer` [retroarch:netplay]
    * HostMethod: `string` [retroarch:netplay]
    * MitmAddress: `string` [retroarch:netplay]
    * MitmPort: `integer` [retroarch:netplay]
    * Country: `string` [retroarch:netplay]
* level:
  * game_type: `object` ([game_type](#game_type))
  * game_mode: `object` ([game_mode](#game_mode))
  * map: `object` ([map](#map))
  * rules: *object* (game specific)
    * time_limit: `integer` [bigboat:battlezone_98_redux]
    * lives: `integer` [bigboat:battlezone_98_redux]
    * satellite: `bool` [bigboat:battlezone_98_redux]
    * barracks: `bool` [bigboat:battlezone_98_redux]
    * sniper: `bool` [bigboat:battlezone_98_redux]
    * splinter: `bool` [bigboat:battlezone_98_redux]
    * game_type: `object` ([game_type](#game_type)) **not used by anything but possible**
    * game_mode: `object` ([game_mode](#game_mode)) **not used by anything but possible**
  * other: *object* (game specific)
    * crc32: `string` [bigboat:battlezone_98_redux] **likely needs adjustment**
    * GameName: `string` [retroarch:netplay]
    * GameCRC: `string` [retroarch:netplay]
    * ID: `string` [retroarch:netplay]
* teams:
  * *key*:
    * max: `integer`
    * human: `bool`
    * computer: `bool`
* time:
  * seconds: `integer`
  * resolution: `integer`
  * max: `bool`
  * context: `string` (enum [session_state](#session_state))
* players: `[object]` ([player](#player))
* player_types: `[object]` ([!player_type](#player_type))
* player_count:
  * *key*: `integer`
* other: *object* (game specific)
  * sync_join: `bool` [bigboat:battlezone_98_redux]
  * meta_data_version: `string` [bigboat:battlezone_98_redux]
  * sync_script: `bool` [bigboat:battlezone_98_redux] (sync handled by script)
  * tps: `integer` [bigboat:battlezone_combat_commander]
  * max_ping: `integer` [bigboat:battlezone_combat_commander]
  * worst_ping: `integer` [bigboat:battlezone_combat_commander]
  * nat_type: `string` [bigboat:battlezone_combat_commander]
  * Core: `object` [retroarch:netplay]
    * Name: `string` [retroarch:netplay]
    * Version: `string` [retroarch:netplay]
    * SubsystemName: `string` [retroarch:netplay]
  * RetroArchVersion: `string` [retroarch:netplay]
  * UnsortedAttributes: `object` [retroarch:netplay]
    * Username: `string` [retroarch:netplay]
    * RoomID: `integer` [retroarch:netplay]
    * Frontend: `string` [retroarch:netplay]
    * CreatedAt: `string` [retroarch:netplay]
    * UpdatedAt: `string` [retroarch:netplay]


## !player_type
  * types: `[string]`
  * max: `integer`

## source
* name: `string`
* status: `string` (enum?) **not final**
* success: `bool` **not final**
* timestamp: `string` (timestamp) **not final**

## player
* name: `string`
* type: `string`
* index: `integer`
* ids:
  * *key*: ("slot", "bzr_net", "steam", "gog")
    * id: `string`
    * raw: `string`
    * identity: `object` ([identity/steam](#identitysteam) or [identity/gog](#identitygog))
* team:
  * id: `string`
  * leader: `bool`
  * index: `integer`
* is_host: `bool`
* stats: *object* (game specific)
  * kills: `integer` [bigboat:battlezone_combat_commander]
  * deaths: `integer` [bigboat:battlezone_combat_commander]
  * score: `integer` [bigboat:battlezone_combat_commander]
* hero: `object` ([hero](#hero))
* other: *object* (game specific)
  * launched: `bool` [bigboat:battlezone_98_redux]
  * is_auth: `bool` [bigboat:battlezone_98_redux]
  * wan_address: `string` [bigboat:battlezone_98_redux] (admin mode only)
  * lan_addresses: `[string]` [bigboat:battlezone_98_redux] (admin mode only)
  * community_patch: `[string]` [bigboat:battlezone_98_redux]
  * community_patch_shim: `[string]` [bigboat:battlezone_98_redux]

## mod
* name: `string`
* image: `string` (url)
* url: `string` (url)
* dependencies: `[object]` ([mod](#mod))

## map
* name: `string`
* description: `string`
* image: `string` (url)
* map_file: `string` (filename)
* game_type: `object` ([game_type](#game_type))
* game_mode: `object` ([game_mode](#game_mode))
* game_balance: `object` ([game_balance](#game_balance))
* teams:
  * *key*:
    * name: `string`
* allowed_heroes: `[object]` ([hero](#hero))

## hero
* name: `string`
* description: `string`
* faction: `object` ([faction](#faction))

## game_balance
* name: `string`
* abbr: `string` (short)
* note: `string`

## game_type
* name: `string`
* icon: `string` (url)
* color: `string` (color) (not forground or background, just "the color")
* color_f: `string` (color) (forground) **not sure if we will keep these**
* color_b: `string` (color) (background) **not sure if we will keep these**
* color_df: `string` (color) (forground for dark) **not sure if we will keep these**
* color_db: `string` (color) (background for dark) **not sure if we will keep these**
* color_lf: `string` (color) (forground for light) **not sure if we will keep these**
* color_lb: `string` (color) (background for light) **not sure if we will keep these**

## game_mode
* name: `string`
* icon: `string` (url)
* color: `string` (color) (not forground or background, just "the color")
* color_f: `string` (color) (forground) **not sure if we will keep these**
* color_b: `string` (color) (background) **not sure if we will keep these**
* color_df: `string` (color) (forground for dark) **not sure if we will keep these**
* color_db: `string` (color) (background for dark) **not sure if we will keep these**
* color_lf: `string` (color) (forground for light) **not sure if we will keep these**
* color_lb: `string` (color) (background for light) **not sure if we will keep these**

## identity/steam
* type: `string` "steam"
* avatar_url: `string` (url)
* nickname: `string`
* profile_url: `string` (url)

## identity/gog
* type: `string` "gog"
* avatar_url: `string` (url)
* username: `string`
* profile_url: `string` (url)

## faction
* name: `string`
* abbr: `string` (short)
* block: `string` (url)
