import {
  Bell,
  CalendarDays,
  ChevronDown,
  ChevronRight,
  Compass,
  ExternalLink,
  Home,
  Library,
  Minus,
  MoreHorizontal,
  PanelLeftClose,
  PanelLeftOpen,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings2,
  Star,
  Users,
  X,
} from 'lucide-react';
import { useRef, useState } from 'react';

const mainCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587-qQTzQnEJJ3oB.jpg';
const secondCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx182255-butzrqd4I0aC.jpg';
const miniCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx170068-ijY3tCP8KoWP.jpg';
const banner = 'https://s4.anilist.co/file/anilistcdn/media/anime/banner/154587-ivXNJ23SM1xB.jpg';

const directionNames = [
  'Editorial ledger', 'Cinematic index', 'Quiet library', 'Dense signal', 'Expressive gallery',
  'Midnight archive', 'Complete archive', 'Balanced archive', 'Cinematic dossier', 'Compact index',
  'Aurora archive', 'Midnight garden', 'Signal cinema',
];

const navigation = [
  { label: 'Home', icon: Home },
  { label: 'Library', icon: Library, active: true },
  { label: 'Calendar', icon: CalendarDays },
  { label: 'Discover', icon: Compass },
  { label: 'Sync center', icon: RefreshCw },
];

const tabs = ['Overview', 'Episodes', 'Progress', 'Providers', 'Franchise', 'Characters', 'Details'];

type RefinedDirection = 8 | 9 | 10;

function DirectionSwitcher({ direction }: { direction: RefinedDirection }) {
  return (
    <nav aria-label="Design directions" className="flex flex-wrap items-center gap-1">
      <span className="mr-3 text-xs font-semibold text-white/45">Direction</span>
      {directionNames.map((name, index) => {
        const number = index + 1;
        return <a key={name} href={`/${number}`} aria-label={`${number}: ${name}`} aria-current={direction === number ? 'page' : undefined} className={`inline-flex min-h-9 min-w-9 items-center justify-center px-3 text-sm font-semibold focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#efb85b] ${direction === number ? 'bg-white text-[#080b11]' : 'text-white/48 hover:bg-white/8 hover:text-white'}`}>{number}</a>;
      })}
    </nav>
  );
}

function AccountMenu() {
  return (
    <details className="group relative">
      <summary aria-label="Open account menu for Kael Ardent" className="flex size-11 cursor-pointer list-none items-center justify-center overflow-hidden rounded-full bg-[#efb85b] text-sm font-black text-[#171006] outline-none ring-2 ring-transparent hover:ring-white/35 focus-visible:ring-[#efb85b] [&::-webkit-details-marker]:hidden">K</summary>
      <div className="absolute right-0 z-30 mt-3 w-64 border border-white/18 bg-[#11151d] p-2 shadow-xl shadow-black/45">
        <div className="px-3 py-3"><p className="font-bold">Kael Ardent</p><p className="mt-1 text-xs text-white/42">Personal archive controls</p></div>
        <div className="border-t border-white/12 pt-2"><a href="/settings" className="flex min-h-10 items-center gap-3 px-3 text-sm text-white/65 hover:bg-white/6 hover:text-white"><Settings2 size={16} /> Settings</a><button className="flex min-h-10 w-full items-center px-3 text-left text-sm text-white/65 hover:bg-white/6 hover:text-white">Log out</button></div>
      </div>
    </details>
  );
}

function ResponsiveHeader({ compact }: { compact: boolean }) {
  return (
    <header className="sticky top-0 z-20 border-b border-white/12 bg-[#080b11]/95 px-4 py-3 backdrop-blur-md sm:px-7 xl:px-9">
      <div className="flex items-center gap-3">
        <a href="/" className="flex shrink-0 items-center gap-2 font-black lg:hidden"><span className="inline-flex size-8 items-center justify-center bg-[#efb85b] text-xs text-[#171006]">C</span><span className="hidden sm:inline">Cantaro</span></a>
        <a href="/search" className={`relative hidden min-w-0 flex-1 items-center border border-white/16 bg-white/[0.025] text-sm text-white/38 hover:border-white/30 hover:text-white/60 md:flex ${compact ? 'max-w-md' : 'max-w-2xl'}`}>
          <Search className="ml-3 shrink-0" size={18} /><span className="truncate px-3 py-3">Search anime, series, movies…</span>
        </a>
        <div className="ml-auto flex shrink-0 items-center gap-1 sm:gap-2"><a href="/search?group=all&preview=true" aria-label="Search Cantaro" className="inline-flex size-11 items-center justify-center text-white/48 hover:bg-white/6 hover:text-white md:hidden"><Search size={18} /></a><button aria-label="Notifications" className="inline-flex size-11 items-center justify-center text-white/48 hover:bg-white/6 hover:text-white"><Bell size={18} /></button><AccountMenu /></div>
      </div>
    </header>
  );
}

function Sidebar({ compact, onToggle }: { compact: boolean; onToggle: () => void }) {
  return (
    <aside className={`sticky top-0 hidden h-screen overflow-y-auto border-r border-white/12 px-4 py-6 lg:flex lg:flex-col ${compact ? 'items-center' : ''}`}>
      <a href="/" aria-label="Cantaro home" className={`flex items-center font-black ${compact ? 'justify-center' : 'gap-3 text-xl'}`}><span className="inline-flex size-9 items-center justify-center bg-[#efb85b] text-sm text-[#171006]">C</span>{compact ? null : 'Cantaro'}</a>
      <nav aria-label="Primary navigation" className="mt-12 w-full space-y-1">
        {navigation.map(({ label, icon: Icon, active }) => <a key={label} href="/media/library" title={compact ? label : undefined} aria-label={compact ? label : undefined} aria-current={active ? 'page' : undefined} className={`flex min-h-11 items-center border-l-2 text-sm font-semibold ${compact ? 'justify-center px-2' : 'gap-3 px-3'} ${active ? 'border-[#efb85b] bg-white/6 text-white' : 'border-transparent text-white/48 hover:text-white'}`}><Icon size={18} />{compact ? null : label}</a>)}
      </nav>
      {!compact ? <div className="mt-10 w-full border-t border-white/12 pt-6"><p className="px-3 text-xs font-semibold uppercase tracking-[0.15em] text-white/32">Your library</p>{['Anime', 'TV series', 'Movies', 'Watch later', 'Favorites'].map(label => <a key={label} href="/media/library" className="block min-h-10 px-3 py-2.5 text-sm text-white/48 hover:text-white">{label}</a>)}</div> : null}
      <button type="button" onClick={onToggle} aria-label={compact ? 'Expand sidebar' : 'Collapse sidebar'} className={`mt-auto flex min-h-11 w-full items-center border-t border-white/12 pt-4 text-sm font-semibold text-white/45 hover:text-white ${compact ? 'justify-center' : 'gap-3 px-3'}`}>{compact ? <PanelLeftOpen size={18} /> : <><PanelLeftClose size={18} /> Collapse sidebar</>}</button>
    </aside>
  );
}

function ProgressControl({ dense = false }: { dense?: boolean }) {
  return (
    <section aria-labelledby={`progress-heading-${dense ? 'dense' : 'regular'}`} className="py-6">
      <div className="flex items-end justify-between gap-4"><div><p id={`progress-heading-${dense ? 'dense' : 'regular'}`} className="text-xs font-semibold uppercase tracking-[0.14em] text-white/38">Your progress</p><p className="mt-2 text-3xl font-black">22 <span className="text-lg text-white/32">/ 28</span></p></div><span className="font-bold text-[#efb85b]">79%</span></div>
      <div className="mt-5 grid grid-cols-[2.5rem_minmax(0,1fr)_2.5rem] items-center gap-3">
        <button aria-label="Decrease watched episodes" className="inline-flex size-10 items-center justify-center text-white/50 hover:bg-white/6 hover:text-white focus-visible:outline-2 focus-visible:outline-[#efb85b]"><Minus size={18} /></button>
        <div className="h-1.5 bg-white/14" aria-label="22 of 28 episodes watched" role="progressbar" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}><div className="h-full w-[78.57%] bg-[#efb85b]" /></div>
        <button aria-label="Increase watched episodes" className="inline-flex size-10 items-center justify-center text-white/50 hover:bg-white/6 hover:text-white focus-visible:outline-2 focus-visible:outline-[#efb85b]"><Plus size={18} /></button>
      </div>
      <div className="mx-[3.25rem] mt-1.5 flex justify-between gap-3 text-xs text-white/36"><span>22 watched</span><span>6 remaining</span></div>
      <div className={`mt-6 border-t border-white/12 pt-5 ${dense ? 'space-y-3' : 'space-y-4'}`}><div className="flex items-center justify-between"><span className="text-sm text-white/48">Your score</span><span className="flex items-center gap-2 font-bold"><Star size={17} fill="currentColor" className="text-[#efb85b]" /> 9 / 10</span></div><div className="flex items-center gap-2 text-xs text-white/45"><RefreshCw size={14} className="text-[#efb85b]" /> AniList progress is current</div></div>
    </section>
  );
}

function HeroStory({ direction }: { direction: RefinedDirection }) {
  const compact = direction === 10;
  const posterDialogRef = useRef<HTMLDialogElement | null>(null);
  const openPoster = () => posterDialogRef.current?.showModal();
  return (
    <div className={`relative min-w-0 ${direction === 9 ? 'overflow-hidden px-5 pb-7 pt-52 sm:px-7 md:py-7' : ''}`}>
      {direction === 9 ? <><div className="absolute inset-x-0 top-0 h-48 bg-cover bg-center opacity-20" style={{ backgroundImage: `url(${banner})` }} /><div className="absolute inset-x-0 top-0 h-52 bg-linear-to-b from-[#080b11]/20 to-[#080b11]" /></> : null}
      {direction === 9 ? <button type="button" onClick={openPoster} aria-label="View full poster" className="absolute inset-x-0 top-0 z-10 h-48 text-left md:hidden"><span className="absolute right-4 bottom-5 border border-white/25 bg-[#080b11]/78 px-3 py-2 text-xs font-semibold text-white/75 backdrop-blur-sm">View poster</span></button> : null}
      <div className={`relative grid items-start gap-7 ${compact ? 'md:grid-cols-[10.5rem_minmax(0,1fr)]' : direction === 9 ? 'md:grid-cols-[16rem_minmax(0,1fr)]' : 'md:grid-cols-[13.5rem_minmax(0,1fr)]'}`}>
        {direction === 9 ? <button type="button" onClick={openPoster} aria-label="View full poster" className="mx-auto hidden w-full max-w-56 self-start border border-white/14 bg-[#11151d] p-2 text-left hover:border-white/35 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#efb85b] md:block md:max-w-none"><img src={mainCover} alt="Frieren: Beyond Journey’s End cover" className="h-auto w-full object-contain" /></button> : <div className={`mx-auto w-full self-start border border-white/14 bg-[#11151d] p-2 md:max-w-none ${compact ? 'max-w-44' : 'max-w-56'}`}><img src={mainCover} alt="Frieren: Beyond Journey’s End cover" className="h-auto w-full object-contain" /></div>}
        <div className="min-w-0 self-center"><p className="text-sm font-semibold text-[#efb85b]">TV series · Fantasy · Adventure</p><h1 id={`refined-title-${direction}`} className={`mt-3 font-black leading-[1.03] tracking-[-0.04em] ${compact ? 'text-3xl sm:text-4xl xl:text-5xl' : 'text-4xl sm:text-5xl lg:text-4xl xl:text-5xl 2xl:text-6xl'}`}>Frieren: Beyond Journey’s End</h1><p className="mt-4 text-sm text-white/46">2023 · 28 episodes · 24 min · 9.1 community score</p><p className={`mt-5 max-w-3xl leading-7 text-white/66 ${compact ? 'text-sm' : 'text-base'}`}>After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.</p><div className="mt-7 flex flex-wrap gap-3"><button className="inline-flex min-h-12 items-center gap-2 bg-[#efb85b] px-5 font-bold text-[#181005] hover:bg-[#ffd48c]"><Play size={18} fill="currentColor" /> Play episode 23</button><button className="inline-flex min-h-12 items-center gap-2 border border-white/22 px-4 font-semibold text-white/75 hover:border-white/45 hover:text-white"><Plus size={18} /> Add to list</button><button aria-label="More title actions" className="inline-flex size-12 items-center justify-center text-white/48 hover:text-white"><MoreHorizontal size={20} /></button></div></div>
      </div>
      {direction === 9 ? <dialog ref={posterDialogRef} aria-label="Frieren poster" className="m-auto max-h-[92vh] max-w-[min(92vw,34rem)] border border-white/18 bg-[#080b11] p-3 text-white backdrop:bg-black/85"><div className="flex justify-end pb-2"><button type="button" onClick={() => posterDialogRef.current?.close()} aria-label="Close poster" className="inline-flex size-11 items-center justify-center text-white/55 hover:bg-white/8 hover:text-white"><X size={20} /></button></div><img src={mainCover} alt="Frieren: Beyond Journey’s End full poster" className="max-h-[78vh] w-auto object-contain" /></dialog> : null}
    </div>
  );
}

function DetailNavigation() {
  return <div className="flex items-start justify-between gap-5 border-y border-white/15"><nav aria-label="Title details" className="flex min-w-0 flex-1 gap-7 overflow-x-auto">{tabs.map((tab, index) => <a key={tab} href="#refined-overview" aria-current={index === 0 ? 'page' : undefined} className={`min-h-14 shrink-0 border-b-2 py-5 text-sm font-semibold ${index === 0 ? 'border-[#efb85b] text-white' : 'border-transparent text-white/43 hover:text-white'}`}>{tab}</a>)}</nav><details className="group relative shrink-0 py-2"><summary className="flex min-h-10 cursor-pointer list-none items-center gap-2 px-2 text-sm font-semibold text-white/50 hover:text-white [&::-webkit-details-marker]:hidden"><Settings2 size={17} /> Manage <ChevronDown size={15} className="transition-transform group-open:rotate-180 motion-reduce:transition-none" /></summary><div className="absolute right-0 z-20 mt-2 w-[min(20rem,calc(100vw-2rem))] border border-white/18 bg-[#11151d] p-5 shadow-xl shadow-black/45"><p className="font-semibold">Secondary controls</p><label className="mt-4 block text-sm text-white/55">Episode sort<select className="mt-2 min-h-10 w-full border border-white/18 bg-[#080b11] px-3 text-white"><option>Newest first</option><option>Oldest first</option></select></label><label className="mt-4 flex min-h-9 items-center gap-3 text-sm text-white/68"><input type="checkbox" defaultChecked className="size-4 accent-[#efb85b]" /> Hide watched episodes</label></div></details></div>;
}

const watchProviders = [
  { name: 'Crunchyroll', detail: 'Watch with subtitles or dub', destination: 'Open series', href: 'https://www.crunchyroll.com/' },
  { name: 'Netflix', detail: 'Available in your region', destination: 'Open Netflix', href: 'https://www.netflix.com/' },
];

function ProviderLinks({ singleColumn = false }: { singleColumn?: boolean }) {
  return <section aria-labelledby="refined-watch"><div className="flex items-baseline justify-between gap-4"><h2 id="refined-watch" className="text-xl font-bold">Where to watch</h2><a href="#providers" className="text-sm text-[#efb85b] hover:text-[#ffd48c]">All destinations</a></div><ul className={`mt-5 grid gap-x-8 ${singleColumn ? '' : 'sm:grid-cols-2'}`}>{watchProviders.map(provider => <li key={provider.name} className="border-t border-white/10"><a href={provider.href} target="_blank" rel="noreferrer" className="group flex min-h-20 items-center gap-3 py-4"><span className="inline-flex size-10 shrink-0 items-center justify-center bg-white/8 font-black text-[#efb85b]">{provider.name[0]}</span><span className="min-w-0 flex-1"><span className="block font-semibold group-hover:text-[#efb85b]">{provider.name}</span><span className="mt-1 block text-xs text-white/42">{provider.detail}</span></span><span className="hidden text-xs text-white/35 xl:inline">{provider.destination}</span><ExternalLink size={16} className="shrink-0 text-white/35 group-hover:text-[#efb85b]" /></a></li>)}</ul></section>;
}

const franchise = [
  { title: 'Beyond Journey’s End', relation: 'Main story · watching', cover: mainCover },
  { title: 'Season 2', relation: 'Sequel · next in order', cover: secondCover },
  { title: 'Mini Anime', relation: 'Side story · 12 shorts', cover: miniCover },
];

function FranchiseStrip({ cinematic = false }: { cinematic?: boolean }) {
  return <section aria-labelledby="refined-franchise"><div className="flex items-baseline justify-between gap-4"><h2 id="refined-franchise" className="text-xl font-bold">Franchise order</h2><a href="#franchise" className="text-sm text-[#efb85b] hover:text-[#ffd48c]">View full franchise</a></div><ol className="mt-5 flex gap-5 overflow-x-auto pb-3">{franchise.map((item, index) => <li key={item.title} className={`group grid shrink-0 grid-cols-[auto_minmax(0,1fr)] gap-3 border-t border-white/12 pt-4 ${cinematic ? 'w-80' : 'w-64'}`}><img src={item.cover} alt="" className={`${cinematic ? 'h-40 w-28' : 'h-28 w-20'} object-cover`} /><span className="flex min-w-0 flex-col py-1"><span className="font-mono text-xs text-white/28">0{index + 1}</span><strong className="mt-3 leading-5 group-hover:text-[#efb85b]">{item.title}</strong><span className="mt-1 text-xs text-white/40">{item.relation}</span><ChevronRight size={17} className="mt-auto text-white/30" /></span></li>)}</ol></section>;
}

function Connections() {
  return <section aria-labelledby="connections-heading"><h2 id="connections-heading" className="text-xl font-bold">Connections</h2><dl className="mt-4 divide-y divide-white/10 text-sm"><div className="flex justify-between gap-4 py-3"><dt className="text-white/42">AniList</dt><dd className="text-white/72">Progress synced · 12m</dd></div><div className="flex justify-between gap-4 py-3"><dt className="text-white/42">AnimeSchedule</dt><dd className="text-white/72">Release schedule</dd></div></dl></section>;
}

function Information() {
  return <section aria-labelledby="refined-information"><h2 id="refined-information" className="text-xl font-bold">Information</h2><dl className="mt-4 divide-y divide-white/10 text-sm">{[['Format', 'TV series'], ['Status', 'Finished'], ['Aired', 'Sep 2023 – Mar 2024'], ['Studio', 'Madhouse'], ['Source', 'Manga'], ['Genres', 'Adventure, Fantasy']].map(([term, value]) => <div key={term} className="grid grid-cols-[5.25rem_1fr] gap-3 py-3"><dt className="text-white/38">{term}</dt><dd className="font-medium text-white/72">{value}</dd></div>)}</dl></section>;
}

function Community() {
  return <section aria-labelledby="refined-community"><h2 id="refined-community" className="text-xl font-bold">Community</h2><dl className="mt-5 space-y-4"><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-white/42"><Star size={15} /> Score</dt><dd className="font-bold">9.1 · 48k</dd></div><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-white/42"><Users size={15} /> Popularity</dt><dd className="font-bold">#2</dd></div><div className="flex justify-between"><dt className="text-sm text-white/42">Favorites</dt><dd className="font-bold">31,204</dd></div></dl></section>;
}

function Characters() {
  return <section aria-labelledby="refined-characters"><div className="flex items-baseline justify-between"><h2 id="refined-characters" className="text-xl font-bold">Main characters</h2><a href="#characters" className="text-sm text-[#efb85b]">See all</a></div><ul className="mt-5 grid gap-x-6 sm:grid-cols-2 xl:grid-cols-3">{[['Frieren', 'Main · Mage'], ['Fern', 'Main · Mage'], ['Stark', 'Main · Warrior'], ['Himmel', 'Supporting · Hero'], ['Heiter', 'Supporting · Priest'], ['Eisen', 'Supporting · Warrior']].map(([name, role]) => <li key={name} className="flex items-center gap-3 border-t border-white/10 py-4"><span className="inline-flex size-10 items-center justify-center border border-white/14 text-sm font-bold text-white/52">{name.slice(0, 2).toUpperCase()}</span><span><strong className="block">{name}</strong><span className="text-xs text-white/40">{role}</span></span></li>)}</ul></section>;
}

function Overview({ direction }: { direction: RefinedDirection }) {
  if (direction === 10) return <div id="refined-overview" className="grid divide-y divide-white/12 lg:grid-cols-3 lg:divide-x lg:divide-y-0"><div className="py-8 lg:pr-7"><ProviderLinks singleColumn /><div className="mt-8 border-t border-white/12 pt-8"><Connections /></div></div><div className="py-8 lg:px-7"><FranchiseStrip /></div><div className="space-y-8 py-8 lg:pl-7"><Information /><div className="border-t border-white/12 pt-8"><Community /></div></div><div className="py-8 lg:col-span-3 lg:border-t lg:border-white/12"><Characters /></div></div>;
  return <div id="refined-overview" className={`grid gap-x-9 ${direction === 8 ? 'xl:grid-cols-[minmax(0,1fr)_20rem]' : 'xl:grid-cols-[minmax(0,1fr)_18rem]'}`}><div className="min-w-0 divide-y divide-white/12"><div className="py-8"><ProviderLinks /></div><div className="py-8"><FranchiseStrip cinematic={direction === 9} /></div><div className="py-8"><Characters /></div></div><aside className="space-y-8 border-t border-white/12 py-8 xl:border-l xl:border-t-0 xl:pl-8"><Information /><div className="border-t border-white/12 pt-8"><Connections /></div><div className="border-t border-white/12 pt-8"><Community /></div></aside></div>;
}

function RefinedShell({ direction }: { direction: RefinedDirection }) {
  const [compactSidebar, setCompactSidebar] = useState(direction === 9);
  const dense = direction === 10;
  return <main className="min-h-screen bg-[#080b11] text-[#f6f3eb]"><div className={`mx-auto grid min-h-screen max-w-[1680px] transition-[grid-template-columns] duration-200 motion-reduce:transition-none ${compactSidebar ? 'lg:grid-cols-[4.75rem_minmax(0,1fr)]' : 'lg:grid-cols-[13.5rem_minmax(0,1fr)]'}`}><Sidebar compact={compactSidebar} onToggle={() => setCompactSidebar(value => !value)} /><div className="min-w-0"><ResponsiveHeader compact={dense} /><div className={`px-4 pb-16 pt-6 sm:px-7 xl:px-9 ${dense ? 'mx-auto max-w-[1480px]' : ''}`}><div className="mb-6 border-b border-white/12 pb-5"><DirectionSwitcher direction={direction} /></div><section aria-labelledby={`refined-title-${direction}`} className={`grid border-b border-white/15 pb-8 ${dense ? 'gap-7 lg:grid-cols-[minmax(0,1fr)_18rem]' : direction === 9 ? 'gap-8 lg:grid-cols-[minmax(0,1fr)_20rem]' : 'gap-9 lg:grid-cols-[minmax(0,1fr)_21rem]'}`}><HeroStory direction={direction} /><aside className="border-t border-white/15 pt-2 lg:border-l lg:border-t-0 lg:pl-8"><ProgressControl dense={dense} /></aside></section><DetailNavigation /><Overview direction={direction} /></div></div></div></main>;
}

export function RefinedArchiveDirection({ direction }: { direction: RefinedDirection }) {
  return <RefinedShell direction={direction} />;
}
