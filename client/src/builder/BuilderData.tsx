import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import {
  builderApi,
  type ItemTemplate,
  type MobTemplate,
  type Quest,
  type RoomDetail,
  type RoomFlagDefinition,
  type WorldSummary,
  type ZoneSummary,
  type ZoneValidation,
} from '../net/builderApi'

/** A collection the change feed can reload. Deduplicated, so a burst costs one of each. */
type Slice = 'worlds' | 'zones' | 'zone' | 'mobs' | 'items' | 'quests'

/**
 * How long the feed has to be quiet before the queued slices are fetched.
 *
 * Short enough that another builder's single edit still appears immediately, long enough that the
 * hundreds of events an import emits arrive as one batch.
 */
const QuietMs = 400

/** The longest a steady stream of writes can hold the refresh off. */
const MaxWaitMs = 3000

/**
 * Shared builder data, loaded once and refreshed on demand. This retires the prop-drilled
 * `onCreated` callbacks and the wholesale refetch-after-every-mutation that the old
 * BuilderScreen scattered across its children - a mutation calls the one relevant `refresh*`
 * and everyone subscribed re-renders.
 *
 * Selection (which world/zone/room) is NOT here - it lives in the URL. Routed components read
 * it from the route and call the loaders below in effects.
 */
interface BuilderDataValue {
  flagDefinitions: RoomFlagDefinition[]
  worlds: WorldSummary[]
  zones: ZoneSummary[]
  rooms: RoomDetail[]
  mobTemplates: MobTemplate[]
  itemTemplates: ItemTemplate[]
  quests: Quest[]
  validation: ZoneValidation | null

  refreshWorlds: () => Promise<void>
  loadZones: (worldKey: string | null) => Promise<void>
  loadZone: (zoneKey: string | null) => Promise<void>
  refreshMobTemplates: () => Promise<void>
  refreshItemTemplates: () => Promise<void>
  refreshQuests: () => Promise<void>
}

const BuilderDataContext = createContext<BuilderDataValue | null>(null)

export function useBuilderData(): BuilderDataValue {
  const ctx = useContext(BuilderDataContext)
  if (!ctx) {
    throw new Error('useBuilderData must be used within <BuilderDataProvider>')
  }
  return ctx
}

export function BuilderDataProvider({ children }: { children: ReactNode }) {
  const [flagDefinitions, setFlagDefinitions] = useState<RoomFlagDefinition[]>([])
  const [worlds, setWorlds] = useState<WorldSummary[]>([])
  const [zones, setZones] = useState<ZoneSummary[]>([])
  const [rooms, setRooms] = useState<RoomDetail[]>([])
  const [mobTemplates, setMobTemplates] = useState<MobTemplate[]>([])
  const [itemTemplates, setItemTemplates] = useState<ItemTemplate[]>([])
  const [quests, setQuests] = useState<Quest[]>([])
  const [validation, setValidation] = useState<ZoneValidation | null>(null)

  // The keys currently loaded, so the change feed can reload the right slice without the
  // routed components having to re-register anything.
  const activeWorld = useRef<string | null>(null)
  const activeZone = useRef<string | null>(null)

  const refreshWorlds = useCallback(async () => {
    setWorlds(await builderApi.worlds().catch(() => []))
  }, [])

  const loadZones = useCallback(async (worldKey: string | null) => {
    activeWorld.current = worldKey
    if (!worldKey) {
      setZones([])
      return
    }
    setZones(await builderApi.zones(worldKey).catch(() => []))
  }, [])

  const loadZone = useCallback(async (zoneKey: string | null) => {
    activeZone.current = zoneKey
    if (!zoneKey) {
      setRooms([])
      setValidation(null)
      return
    }
    const [loadedRooms, loadedValidation] = await Promise.all([
      builderApi.rooms(zoneKey).catch(() => []),
      builderApi.validate(zoneKey).catch(() => null),
    ])
    setRooms(loadedRooms)
    setValidation(loadedValidation)
  }, [])

  const refreshMobTemplates = useCallback(async () => {
    setMobTemplates(await builderApi.mobTemplates().catch(() => []))
  }, [])

  const refreshItemTemplates = useCallback(async () => {
    setItemTemplates(await builderApi.itemTemplates().catch(() => []))
  }, [])

  const refreshQuests = useCallback(async () => {
    setQuests(await builderApi.quests().catch(() => []))
  }, [])

  // One-time loads that never depend on selection.
  useEffect(() => {
    void builderApi.roomFlags().then(setFlagDefinitions).catch(() => undefined)
    void refreshWorlds()
    void refreshMobTemplates()
    void refreshItemTemplates()
    void refreshQuests()
  }, [refreshWorlds, refreshMobTemplates, refreshItemTemplates, refreshQuests])

  // The builder change feed. A second builder's write lands here as an entity-changed event;
  // we reload the affected slice. Degrades silently if the endpoint is absent - EventSource
  // just retries, and the app works without it (PLAN §2).
  //
  // <b>Coalesced, and an import is why.</b> The server emits one event per changed entity and
  // every branch below refetches a whole collection, which is the right trade for somebody
  // editing one room at a time and catastrophic for a bulk change: importing the world moves 224
  // rooms, 100 spawners, 93 items, 69 mobs and 35 quests, so the feed asked for the full quest
  // list thirty-five times and the full room list three hundred. Eight hundred-odd requests
  // against a bucket of 120 refilling at 20 a second is a wall of 429s, and the reads that did
  // get through were the ones that happened to catch a token.
  //
  // So events accumulate into a set of slices to reload and the fetches happen once the feed goes
  // quiet. Deduplication is the whole mechanism: thirty-five quest events and one are the same
  // request, because the request was never about which quest changed.
  useEffect(() => {
    let source: EventSource
    try {
      source = new EventSource('/api/builder/stream')
    } catch {
      return
    }

    const pending = new Set<Slice>()
    let quiet: ReturnType<typeof setTimeout> | undefined
    let deadline: ReturnType<typeof setTimeout> | undefined

    const flush = () => {
      clearTimeout(quiet)
      clearTimeout(deadline)
      quiet = undefined
      deadline = undefined

      const slices = [...pending]
      pending.clear()

      // Read at flush time rather than when the event arrived: during a long burst the builder may
      // have navigated, and the slice worth loading is the one they are looking at now.
      for (const slice of slices) {
        switch (slice) {
          case 'worlds':
            void refreshWorlds()
            break
          case 'zones':
            if (activeWorld.current) void loadZones(activeWorld.current)
            break
          case 'zone':
            if (activeZone.current) void loadZone(activeZone.current)
            break
          case 'mobs':
            void refreshMobTemplates()
            break
          case 'items':
            void refreshItemTemplates()
            break
          case 'quests':
            void refreshQuests()
            break
        }
      }
    }

    const queue = (...slices: Slice[]) => {
      for (const slice of slices) pending.add(slice)

      // Trailing, so a burst costs one round of requests instead of one per event. A lone edit by
      // another builder still lands within a blink.
      clearTimeout(quiet)
      quiet = setTimeout(flush, QuietMs)

      // ...but not indefinitely. A steady trickle of writes would otherwise keep pushing the
      // trailing timer out and the screen would never update at all, which is worse than being a
      // moment late.
      deadline ??= setTimeout(flush, MaxWaitMs)
    }

    const onChange = (event: MessageEvent) => {
      let payload: { kind?: string; key?: string }
      try {
        payload = JSON.parse(event.data)
      } catch {
        return
      }

      // Kinds match the server's WorldChange.EntityKind (lowercase, kebab-case).
      switch (payload.kind) {
        case 'world':
          queue('worlds', 'zones')
          break
        case 'zone':
          queue('zones', 'zone')
          break
        case 'room':
        case 'exit':
        case 'spawner':
          queue('zone')
          break
        case 'mob-template':
          queue('mobs')
          break
        case 'item-template':
          queue('items')
          break
        case 'quest':
          queue('quests')
          break
      }
    }

    source.addEventListener('entity-changed', onChange as EventListener)
    return () => {
      clearTimeout(quiet)
      clearTimeout(deadline)
      source.close()
    }
  }, [refreshWorlds, loadZones, loadZone, refreshMobTemplates, refreshItemTemplates, refreshQuests])

  const value = useMemo<BuilderDataValue>(
    () => ({
      flagDefinitions,
      worlds,
      zones,
      rooms,
      mobTemplates,
      itemTemplates,
      quests,
      validation,
      refreshWorlds,
      loadZones,
      loadZone,
      refreshMobTemplates,
      refreshItemTemplates,
      refreshQuests,
    }),
    [
      flagDefinitions,
      worlds,
      zones,
      rooms,
      mobTemplates,
      itemTemplates,
      quests,
      validation,
      refreshWorlds,
      loadZones,
      loadZone,
      refreshMobTemplates,
      refreshItemTemplates,
      refreshQuests,
    ],
  )

  return <BuilderDataContext.Provider value={value}>{children}</BuilderDataContext.Provider>
}
