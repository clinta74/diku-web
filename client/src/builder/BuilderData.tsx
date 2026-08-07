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
  type RoomDetail,
  type RoomFlagDefinition,
  type WorldSummary,
  type ZoneSummary,
  type ZoneValidation,
} from '../net/builderApi'

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
  validation: ZoneValidation | null

  refreshWorlds: () => Promise<void>
  loadZones: (worldKey: string | null) => Promise<void>
  loadZone: (zoneKey: string | null) => Promise<void>
  refreshMobTemplates: () => Promise<void>
  refreshItemTemplates: () => Promise<void>
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

  // One-time loads that never depend on selection.
  useEffect(() => {
    void builderApi.roomFlags().then(setFlagDefinitions).catch(() => undefined)
    void refreshWorlds()
    void refreshMobTemplates()
    void refreshItemTemplates()
  }, [refreshWorlds, refreshMobTemplates, refreshItemTemplates])

  // The builder change feed. A second builder's write lands here as an entity-changed event;
  // we reload the affected slice. Degrades silently if the endpoint is absent - EventSource
  // just retries, and the app works without it (PLAN §2).
  useEffect(() => {
    let source: EventSource
    try {
      source = new EventSource('/api/builder/stream')
    } catch {
      return
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
          void refreshWorlds()
          if (activeWorld.current) void loadZones(activeWorld.current)
          break
        case 'zone':
          if (activeWorld.current) void loadZones(activeWorld.current)
          if (activeZone.current) void loadZone(activeZone.current)
          break
        case 'room':
        case 'exit':
        case 'spawner':
          if (activeZone.current) void loadZone(activeZone.current)
          break
        case 'mob-template':
          void refreshMobTemplates()
          break
        case 'item-template':
          void refreshItemTemplates()
          break
      }
    }

    source.addEventListener('entity-changed', onChange as EventListener)
    return () => source.close()
  }, [refreshWorlds, loadZones, loadZone, refreshMobTemplates, refreshItemTemplates])

  const value = useMemo<BuilderDataValue>(
    () => ({
      flagDefinitions,
      worlds,
      zones,
      rooms,
      mobTemplates,
      itemTemplates,
      validation,
      refreshWorlds,
      loadZones,
      loadZone,
      refreshMobTemplates,
      refreshItemTemplates,
    }),
    [
      flagDefinitions,
      worlds,
      zones,
      rooms,
      mobTemplates,
      itemTemplates,
      validation,
      refreshWorlds,
      loadZones,
      loadZone,
      refreshMobTemplates,
      refreshItemTemplates,
    ],
  )

  return <BuilderDataContext.Provider value={value}>{children}</BuilderDataContext.Provider>
}
