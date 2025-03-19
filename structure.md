# enum string values
* session_state
  * `unknown` // Normally has a state, but it is unknown
  * `pre_game` // Pre-Game Setup
  * `loading` // Loading Game
  * `in_game` // In-Game
  * `post_game` // Post-Game or GameOver
  * `paused` // unused options so far
  * `away` // unused options so far
  * `busy` // unused options so far
  * `not_responding` // unused options so far

# default
* *type*: `object`

# session
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
* status:
  * is_locked: `bool`
  * has_password: `bool`
  * state: `string` (enum)
  * other: *object* (game specific)
* address: **work needed here**
  * token: `string`
  * other: *object* (game specific)
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
  * other: *object* (game specific)
* teams:
  * *key*:
    * max: `integer`
    * human: `bool`
    * computer: `bool`
* time:
  * seconds: `integer`
  * resolution: `integer`
  * max: `bool`
  * context: `string` (enum session_state)
* players: `[object]` ([player](#player))
* player_types: `[object]` ([!player_type](#player_type))
* player_count:
  * *key*: `integer`
* other: *object* (game specific)

# !player_type
  * types: `[string]`
  * max: `integer`

# source
* name: `string`
* status: `string` (enum?) **not final**
* success: `bool` **not final**
* timestamp: `string` (timestamp) **not final**

# player
* name: `string`
* type: `string`
* index: `integer`
* ids:
  * *key*: ("slot", "bzr_net", "steam", "gog")
    * id: `string`
	* raw: `string`
	* identity: `object` ([identity/steam(#identitysteam) or [identity/gog](#identitygog))
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

# mod
* name: `string`
* image: `string` (url)
* url: `string` (url)
* dependencies: `[object]` ([mod](#mod))

# map
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

# hero
* name: `string`
* description: `string`
* faction: `object` ([faction](#faction))

# game_balance
* name: `string`
* abbr: `string` (short)
* note: `string`

# game_type
* name: `string`
* icon: `string` (url)
* color: `string` (color)
* color_f: `string` (color)
* color_b: `string` (color)
* color_df: `string` (color)
* color_db: `string` (color)
* color_lf: `string` (color)
* color_lb: `string` (color)

# game_mode
* name: `string`
* icon: `string` (url)
* color: `string` (color)
* color_f: `string` (color)
* color_b: `string` (color)
* color_df: `string` (color)
* color_db: `string` (color)
* color_lf: `string` (color)
* color_lb: `string` (color)

# identity/steam
* type: `string` "steam"
* avatar_url: `string` (url)
* nickname: `string`
* profile_url: `string` (url)

# identity/gog
* type: `string` "gog"
* avatar_url: `string` (url)
* username: `string`
* profile_url: `string` (url)

# faction
* name: `string`
* abbr: `string` (short)
* block: `string` (url)
