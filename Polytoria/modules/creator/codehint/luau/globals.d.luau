declare json: {
    parse: ((string?) -> any),
    serialize: ((any?) -> string),
    null: (() -> string),
    isNull: ((string?) -> boolean),
}

declare guid: {
    new: (() -> string),
    empty: (() -> string),
}

declare _POLY_VERSION: string

declare _POLY_2: boolean

declare _LOCALTEST: boolean

declare function wait(seconds: number?)
    
declare function spawn(callback: () -> ())

declare function tick(): number

declare function time(): number

declare function require(moduleScript: (ModuleScript)): any

declare function pcall(f: (...any) -> (boolean, any))

declare world: World

declare script: Script

declare Environment: Environment

declare Lighting: Lighting

declare Players: Players

declare ScriptService: ScriptService

declare Hidden: Hidden

declare ServerHidden: ServerHidden

declare PlayerDefaults: PlayerDefaults

declare PlayerGUI: PlayerGUI

declare Chat: ChatService

declare Filter: FilterService

declare Camera: Camera

declare Assets: AssetsService

declare Input: InputService

declare Achievements: AchievementsService

declare Tween: TweenService

declare CoreUI: CoreUIService

declare Stats: Stats

declare Teams: Teams

declare Datastore: DatastoreService

declare Http: HttpService

declare Insert: InsertService

declare Purchases: PurchasesService

declare Capture: CaptureService

declare Presence: PresenceService

declare Creator: CreatorService

declare CreatorContext: CreatorContextService

declare IO: IOService

declare Addons: CreatorAddons

declare Selections: CreatorSelections

declare History: CreatorHistory

declare CreatorGUI: CreatorGUI

declare Preferences: PreferencesService