import {
  Bell,
  CalendarDays,
  ChevronDown,
  ChevronRight,
  Compass,
  ExternalLink,
  Heart,
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
  Sparkles,
  Star,
  Users,
  X,
} from 'lucide-react';
import { useRef, useState } from 'react';

const mainCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587-qQTzQnEJJ3oB.jpg';
const secondCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx182255-butzrqd4I0aC.jpg';
const miniCover = 'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx170068-ijY3tCP8KoWP.jpg';
const banner = 'https://s4.anilist.co/file/anilistcdn/media/anime/banner/154587-ivXNJ23SM1xB.jpg';

const navigation = [
  { label: 'Home', icon: Home },
  { label: 'Library', icon: Library, active: true },
  { label: 'Calendar', icon: CalendarDays },
  { label: 'Discover', icon: Compass },
  { label: 'Sync center', icon: RefreshCw },
];

const tabs = ['Overview', 'Episodes', 'Progress', 'Providers', 'Franchise', 'Characters', 'Details'];

function DirectionSwitcher() {
  return (
    <nav aria-label="Design directions" className="flex items-center gap-1 overflow-x-auto py-1">
      <span className="mr-3 shrink-0 text-xs font-semibold text-[#aaa0b5]">Direction</span>
      {Array.from({ length: 13 }, (_, index) => index + 1).map(number => (
        <a
          key={number}
          href={`/${number}`}
          aria-current={number === 11 ? 'page' : undefined}
          className={`inline-flex size-9 shrink-0 items-center justify-center text-sm font-bold transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#6de3ff] ${number === 11 ? 'bg-[#ff7465] text-[#210a0e]' : 'text-[#aaa0b5] hover:bg-[#6de3ff]/12 hover:text-[#6de3ff]'}`}
        >
          {number}
        </a>
      ))}
    </nav>
  );
}

function AccountMenu() {
  return (
    <details className="group relative">
      <summary className="flex size-10 cursor-pointer list-none items-center justify-center rounded-full bg-[#a98cff] text-sm font-black text-[#160d25] ring-2 ring-transparent transition-colors hover:bg-[#c3b1ff] focus-visible:ring-[#6de3ff] [&::-webkit-details-marker]:hidden" aria-label="Open account menu for Kael Ardent">K</summary>
      <div className="absolute right-0 z-40 mt-3 w-64 border border-[#a98cff]/30 bg-[#181120] p-2 text-[#fff8f4]">
        <div className="px-3 py-3"><p className="font-bold">Kael Ardent</p><p className="mt-1 text-xs text-[#aaa0b5]">Personal archive controls</p></div>
        <div className="border-t border-[#a98cff]/20 pt-2"><a href="/settings" className="flex min-h-10 items-center gap-3 px-3 text-sm text-[#d6ccd9] hover:bg-[#a98cff]/12 hover:text-[#c3b1ff]"><Settings2 size={16} /> Settings</a><button className="flex min-h-10 w-full items-center px-3 text-left text-sm text-[#d6ccd9] hover:bg-[#ff7465]/12 hover:text-[#ff9b91]">Log out</button></div>
      </div>
    </details>
  );
}

function Header() {
  return (
    <header className="sticky top-0 z-30 border-b border-[#a98cff]/20 bg-[#0c0813]/95 px-4 py-3 backdrop-blur-md sm:px-7 xl:px-9">
      <div className="flex items-center gap-3">
        <a href="/" className="flex shrink-0 items-center gap-2 font-black lg:hidden"><span className="inline-flex size-8 items-center justify-center bg-[#ff7465] text-xs text-[#210a0e]">C</span><span className="hidden sm:inline">Cantaro</span></a>
        <a href="/search" className="hidden min-w-32 max-w-2xl flex-1 items-center border border-[#a98cff]/25 bg-[#181120] text-sm text-[#aaa0b5] transition-colors hover:border-[#6de3ff]/55 hover:text-[#6de3ff] md:flex"><Search className="ml-3 shrink-0" size={18} /><span className="truncate px-3 py-3">Search anime, series, movies…</span></a>
        <div className="ml-auto flex shrink-0 items-center gap-1 sm:gap-2">
          <a href="/search?group=all&preview=true" aria-label="Search Cantaro" className="inline-flex size-10 items-center justify-center text-[#aaa0b5] transition-colors hover:bg-[#6de3ff]/12 hover:text-[#6de3ff] md:hidden"><Search size={18} /></a>
          <button aria-label="Notifications" className="inline-flex size-10 items-center justify-center text-[#aaa0b5] transition-colors hover:bg-[#ff7465]/12 hover:text-[#ff9b91]"><Bell size={18} /></button>
          <AccountMenu />
        </div>
      </div>
    </header>
  );
}

function Sidebar({ compact, onToggle }: { compact: boolean; onToggle: () => void }) {
  return (
    <aside className={`sticky top-0 hidden h-screen overflow-y-auto border-r border-[#a98cff]/20 bg-[#100a18] px-4 py-6 lg:flex lg:flex-col ${compact ? 'items-center' : ''}`}>
      <a href="/" aria-label="Cantaro home" className={`flex items-center font-black ${compact ? 'justify-center' : 'gap-3 text-xl'}`}><span className="inline-flex size-9 items-center justify-center bg-[#ff7465] text-sm text-[#210a0e]">C</span>{compact ? null : 'Cantaro'}</a>
      <nav aria-label="Primary navigation" className="mt-12 w-full space-y-1">
        {navigation.map(({ label, icon: Icon, active }) => (
          <a key={label} href="/media/library" title={compact ? label : undefined} aria-label={compact ? label : undefined} aria-current={active ? 'page' : undefined} className={`flex min-h-11 items-center border-l text-sm font-semibold transition-colors ${compact ? 'justify-center px-2' : 'gap-3 px-3'} ${active ? 'border-[#ff7465] bg-[#ff7465]/10 text-[#ff9b91]' : 'border-transparent text-[#aaa0b5] hover:border-[#6de3ff]/60 hover:bg-[#6de3ff]/8 hover:text-[#6de3ff]'}`}><Icon size={18} />{compact ? null : label}</a>
        ))}
      </nav>
      {!compact ? <div className="mt-10 w-full border-t border-[#a98cff]/18 pt-6"><p className="px-3 text-xs font-semibold uppercase tracking-[0.14em] text-[#706778]">Your library</p>{['Anime', 'TV series', 'Movies', 'Watch later', 'Favorites'].map((label, index) => <a key={label} href="/media/library" className={`block min-h-10 px-3 py-2.5 text-sm transition-colors ${index % 2 === 0 ? 'hover:text-[#c3b1ff]' : 'hover:text-[#6de3ff]'} text-[#aaa0b5]`}>{label}</a>)}</div> : null}
      <button type="button" onClick={onToggle} aria-label={compact ? 'Expand sidebar' : 'Collapse sidebar'} className={`mt-auto flex min-h-11 w-full items-center border-t border-[#a98cff]/18 pt-4 text-sm font-semibold text-[#aaa0b5] transition-colors hover:text-[#c3b1ff] ${compact ? 'justify-center' : 'gap-3 px-3'}`}>{compact ? <PanelLeftOpen size={18} /> : <><PanelLeftClose size={18} /> Collapse sidebar</>}</button>
    </aside>
  );
}

function PosterDialog({ dialogRef }: { dialogRef: React.RefObject<HTMLDialogElement | null> }) {
  return (
    <dialog ref={dialogRef} aria-label="Frieren poster" className="m-auto max-h-[92vh] max-w-[min(92vw,34rem)] border border-[#a98cff]/35 bg-[#0c0813] p-3 text-[#fff8f4] backdrop:bg-[#07030c]/90">
      <div className="flex items-center justify-between pb-2"><span className="text-sm font-semibold text-[#c3b1ff]">Frieren: Beyond Journey’s End</span><button type="button" onClick={() => dialogRef.current?.close()} aria-label="Close poster" className="inline-flex size-11 items-center justify-center text-[#aaa0b5] hover:bg-[#ff7465]/12 hover:text-[#ff9b91]"><X size={20} /></button></div>
      <img src={mainCover} alt="Frieren: Beyond Journey’s End full poster" className="max-h-[78vh] w-auto object-contain" />
    </dialog>
  );
}

function ProgressPanel() {
  return (
    <section aria-labelledby="aurora-progress" className="border-t border-[#a98cff]/20 py-6 lg:border-l lg:border-t-0 lg:pl-8">
      <div className="flex items-end justify-between gap-4"><div><h2 id="aurora-progress" className="text-sm font-bold text-[#c3b1ff]">Your progress</h2><p className="mt-2 text-3xl font-black">22 <span className="text-lg text-[#706778]">/ 28</span></p></div><span className="font-bold text-[#b7f26c]">79%</span></div>
      <div className="mt-5 grid grid-cols-[2.5rem_minmax(0,1fr)_2.5rem] items-center gap-3">
        <button aria-label="Decrease watched episodes" className="inline-flex size-10 items-center justify-center text-[#aaa0b5] transition-colors hover:bg-[#ff7465]/12 hover:text-[#ff9b91] focus-visible:outline-2 focus-visible:outline-[#6de3ff]"><Minus size={18} /></button>
        <div className="h-1.5 bg-[#342b3e]" aria-label="22 of 28 episodes watched" role="progressbar" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}><div className="h-full w-[78.57%] bg-[#b7f26c]" /></div>
        <button aria-label="Increase watched episodes" className="inline-flex size-10 items-center justify-center text-[#aaa0b5] transition-colors hover:bg-[#b7f26c]/12 hover:text-[#b7f26c] focus-visible:outline-2 focus-visible:outline-[#6de3ff]"><Plus size={18} /></button>
      </div>
      <div className="mx-[3.25rem] mt-2 flex justify-between text-xs text-[#706778]"><span>22 watched</span><span>6 remaining</span></div>
      <div className="mt-6 space-y-4 border-t border-[#a98cff]/18 pt-5"><div className="flex items-center justify-between"><span className="text-sm text-[#aaa0b5]">Your score</span><span className="flex items-center gap-2 font-bold"><Star size={17} fill="currentColor" className="text-[#ff7465]" /> 9 / 10</span></div><div className="flex items-center gap-2 text-xs text-[#b7f26c]"><RefreshCw size={14} /> AniList progress is current</div></div>
    </section>
  );
}

function Hero() {
  const posterDialogRef = useRef<HTMLDialogElement | null>(null);
  const openPoster = () => posterDialogRef.current?.showModal();
  return (
    <section aria-labelledby="aurora-title" className="relative overflow-hidden border-b border-[#a98cff]/25 px-4 pb-8 pt-48 sm:px-7 md:pt-8 xl:px-9">
      <div className="absolute inset-x-0 top-0 h-56 bg-cover bg-center opacity-55 md:h-full md:opacity-30" style={{ backgroundImage: `url(${banner})` }} />
      <div className="absolute inset-0 bg-linear-to-b from-[#0c0813]/10 via-[#0c0813]/75 to-[#0c0813] md:bg-linear-to-r md:from-[#0c0813]/80 md:via-[#0c0813]/75 md:to-[#24112b]/75" />
      <div className="absolute right-[8%] top-12 hidden size-48 rounded-full bg-[#6de3ff]/10 blur-3xl md:block" />
      <button type="button" onClick={openPoster} aria-label="View full poster" className="absolute inset-x-0 top-0 z-10 h-44 text-left md:hidden"><span className="absolute bottom-3 right-4 border border-[#6de3ff]/45 bg-[#0c0813]/80 px-3 py-2 text-xs font-semibold text-[#6de3ff] backdrop-blur-sm">View poster</span></button>
      <div className="relative z-10 grid gap-8 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="grid gap-7 md:grid-cols-[14rem_minmax(0,1fr)]">
          <button type="button" onClick={openPoster} aria-label="View full poster" className="group hidden self-start border border-[#a98cff]/30 bg-[#181120] p-2 text-left transition-colors hover:border-[#ff7465] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#6de3ff] md:block"><img src={mainCover} alt="Frieren: Beyond Journey’s End cover" className="h-auto w-full object-contain transition-[filter] duration-200 group-hover:saturate-125 motion-reduce:transition-none" /></button>
          <div className="min-w-0 self-center"><p className="font-semibold text-[#6de3ff]">TV series · Fantasy · Adventure</p><h1 id="aurora-title" className="mt-3 text-4xl font-black leading-[1.03] tracking-[-0.04em] text-[#fff8f4] sm:text-5xl xl:text-6xl">Frieren: Beyond Journey’s End</h1><p className="mt-4 text-sm text-[#aaa0b5]">2023 · 28 episodes · 24 min · 9.1 community score</p><p className="mt-5 max-w-3xl text-base leading-7 text-[#d6ccd9]">After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.</p><div className="mt-7 flex flex-wrap gap-3"><button className="inline-flex min-h-12 items-center gap-2 bg-[#ff7465] px-5 font-bold text-[#210a0e] transition-colors hover:bg-[#ff9b91]"><Play size={18} fill="currentColor" /> Play episode 23</button><button className="inline-flex min-h-12 items-center gap-2 border border-[#a98cff]/40 px-4 font-semibold text-[#d6ccd9] transition-colors hover:border-[#a98cff] hover:bg-[#a98cff]/12 hover:text-[#c3b1ff]"><Plus size={18} /> Add to list</button><button aria-label="More title actions" className="inline-flex size-12 items-center justify-center text-[#aaa0b5] transition-colors hover:bg-[#6de3ff]/12 hover:text-[#6de3ff]"><MoreHorizontal size={20} /></button></div></div>
        </div>
        <ProgressPanel />
      </div>
      <PosterDialog dialogRef={posterDialogRef} />
    </section>
  );
}

function DetailNavigation() {
  return (
    <div className="flex items-start justify-between gap-5 border-b border-[#a98cff]/22">
      <nav aria-label="Title details" className="flex min-w-0 flex-1 gap-7 overflow-x-auto">{tabs.map((tab, index) => <a key={tab} href={index === 0 ? '#aurora-overview' : `#${tab.toLowerCase()}`} aria-current={index === 0 ? 'page' : undefined} className={`min-h-14 shrink-0 border-b-2 py-5 text-sm font-semibold transition-colors ${index === 0 ? 'border-[#ff7465] text-[#ff9b91]' : 'border-transparent text-[#aaa0b5] hover:border-[#6de3ff]/60 hover:text-[#6de3ff]'}`}>{tab}</a>)}</nav>
      <details className="group relative shrink-0 py-2"><summary className="flex min-h-10 cursor-pointer list-none items-center gap-2 px-2 text-sm font-semibold text-[#aaa0b5] hover:text-[#c3b1ff] [&::-webkit-details-marker]:hidden"><Settings2 size={17} /> <span className="hidden sm:inline">Manage</span> <ChevronDown size={15} className="transition-transform group-open:rotate-180 motion-reduce:transition-none" /></summary><div className="absolute right-0 z-20 mt-2 w-[min(20rem,calc(100vw-2rem))] border border-[#a98cff]/30 bg-[#181120] p-5"><p className="font-semibold">Secondary controls</p><label className="mt-4 block text-sm text-[#aaa0b5]">Episode sort<select className="mt-2 min-h-10 w-full border border-[#a98cff]/30 bg-[#0c0813] px-3 text-[#fff8f4]"><option>Newest first</option><option>Oldest first</option></select></label><label className="mt-4 flex min-h-9 items-center gap-3 text-sm text-[#d6ccd9]"><input type="checkbox" defaultChecked className="size-4 accent-[#ff7465]" /> Hide watched episodes</label></div></details>
    </div>
  );
}

const franchise = [
  { title: 'Beyond Journey’s End', relation: 'Main story · watching', cover: mainCover, color: '#b7f26c' },
  { title: 'Season 2', relation: 'Sequel · next in order', cover: secondCover, color: '#6de3ff' },
  { title: 'Mini Anime', relation: 'Side story · 12 shorts', cover: miniCover, color: '#ff7465' },
];

function Providers() {
  const providers = [
    { name: 'Crunchyroll', detail: 'Sub and dub available', href: 'https://www.crunchyroll.com/', accent: 'group-hover:text-[#ff9b91]' },
    { name: 'Netflix', detail: 'Available in your region', href: 'https://www.netflix.com/', accent: 'group-hover:text-[#6de3ff]' },
  ];
  return <section id="providers" aria-labelledby="aurora-providers" className="py-8"><div className="flex items-baseline justify-between gap-4"><h2 id="aurora-providers" className="text-xl font-bold">Where to watch</h2><a href="#providers" className="text-sm text-[#6de3ff] hover:text-[#a9f0ff]">All destinations</a></div><ul className="mt-5 grid gap-x-8 sm:grid-cols-2">{providers.map((provider, index) => <li key={provider.name} className="border-t border-[#a98cff]/18"><a href={provider.href} target="_blank" rel="noreferrer" className="group flex min-h-20 items-center gap-3 py-4"><span className={`inline-flex size-10 shrink-0 items-center justify-center font-black ${index === 0 ? 'bg-[#ff7465]/12 text-[#ff9b91]' : 'bg-[#6de3ff]/12 text-[#6de3ff]'}`}>{provider.name[0]}</span><span className="min-w-0 flex-1"><strong className={`block transition-colors ${provider.accent}`}>{provider.name}</strong><span className="mt-1 block text-xs text-[#aaa0b5]">{provider.detail}</span></span><ExternalLink size={16} className={`shrink-0 text-[#706778] transition-colors ${provider.accent}`} /></a></li>)}</ul></section>;
}

function Franchise() {
  return <section id="franchise" aria-labelledby="aurora-franchise" className="py-8"><div className="flex items-baseline justify-between gap-4"><h2 id="aurora-franchise" className="text-xl font-bold">Franchise order</h2><a href="#franchise" className="text-sm text-[#6de3ff] hover:text-[#a9f0ff]">View full franchise</a></div><ol className="mt-5 flex gap-5 overflow-x-auto pb-3">{franchise.map((item, index) => <li key={item.title} className="group w-72 shrink-0 border-t border-[#a98cff]/22 pt-4"><a href="/media/library" className="grid grid-cols-[6rem_minmax(0,1fr)] gap-4"><img src={item.cover} alt="" className="h-36 w-24 object-cover transition-[filter] duration-200 group-hover:saturate-150 motion-reduce:transition-none" /><span className="flex min-w-0 flex-col py-1"><span className="font-mono text-xs" style={{ color: item.color }}>0{index + 1}</span><strong className="mt-3 leading-5 transition-colors group-hover:text-[#ff9b91]">{item.title}</strong><span className="mt-1 text-xs text-[#aaa0b5]">{item.relation}</span><ChevronRight size={17} className="mt-auto text-[#706778] transition-colors group-hover:text-[#6de3ff]" /></span></a></li>)}</ol></section>;
}

function Characters() {
  const characters = [
    ['Frieren', 'Main · Mage', 'bg-[#a98cff]/15 text-[#c3b1ff]', 'hover:border-[#a98cff] hover:text-[#c3b1ff]'],
    ['Fern', 'Main · Mage', 'bg-[#6de3ff]/12 text-[#6de3ff]', 'hover:border-[#6de3ff] hover:text-[#6de3ff]'],
    ['Stark', 'Main · Warrior', 'bg-[#ff7465]/12 text-[#ff9b91]', 'hover:border-[#ff7465] hover:text-[#ff9b91]'],
    ['Himmel', 'Supporting · Hero', 'bg-[#b7f26c]/12 text-[#b7f26c]', 'hover:border-[#b7f26c] hover:text-[#b7f26c]'],
  ];
  return <section id="characters" aria-labelledby="aurora-characters" className="py-8"><div className="flex items-baseline justify-between"><h2 id="aurora-characters" className="text-xl font-bold">Main characters</h2><a href="#characters" className="text-sm text-[#6de3ff] hover:text-[#a9f0ff]">See all</a></div><ul className="mt-5 grid gap-x-6 sm:grid-cols-2">{characters.map(([name, role, avatar, hover]) => <li key={name} className={`group flex items-center gap-3 border-t border-[#a98cff]/18 py-4 transition-colors ${hover}`}><span className={`inline-flex size-11 items-center justify-center text-sm font-black ${avatar}`}>{name.slice(0, 2).toUpperCase()}</span><span><strong className="block transition-colors group-hover:text-inherit">{name}</strong><span className="text-xs text-[#aaa0b5]">{role}</span></span></li>)}</ul></section>;
}

function SideFacts() {
  return <aside className="space-y-8 border-t border-[#a98cff]/18 py-8 xl:border-l xl:border-t-0 xl:pl-8"><section aria-labelledby="aurora-information"><h2 id="aurora-information" className="text-xl font-bold text-[#c3b1ff]">Information</h2><dl className="mt-4 divide-y divide-[#a98cff]/14 text-sm">{[['Format', 'TV series'], ['Status', 'Finished'], ['Aired', 'Sep 2023 – Mar 2024'], ['Studio', 'Madhouse'], ['Source', 'Manga'], ['Genres', 'Adventure, Fantasy']].map(([term, value]) => <div key={term} className="grid grid-cols-[5.25rem_1fr] gap-3 py-3"><dt className="text-[#aaa0b5]">{term}</dt><dd className="font-medium text-[#fff8f4]">{value}</dd></div>)}</dl></section><section aria-labelledby="aurora-connections" className="border-t border-[#a98cff]/18 pt-8"><h2 id="aurora-connections" className="text-xl font-bold text-[#6de3ff]">Connections</h2><dl className="mt-4 divide-y divide-[#a98cff]/14 text-sm"><div className="flex justify-between gap-4 py-3"><dt className="text-[#aaa0b5]">AniList</dt><dd className="flex items-center gap-2 text-[#b7f26c]"><RefreshCw size={13} /> Current · 12m</dd></div><div className="flex justify-between gap-4 py-3"><dt className="text-[#aaa0b5]">AnimeSchedule</dt><dd className="text-[#6de3ff]">Release schedule</dd></div></dl></section><section aria-labelledby="aurora-community" className="border-t border-[#a98cff]/18 pt-8"><h2 id="aurora-community" className="text-xl font-bold text-[#ff9b91]">Community</h2><dl className="mt-5 space-y-4"><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-[#aaa0b5]"><Star size={15} className="text-[#ff7465]" /> Score</dt><dd className="font-bold">9.1 · 48k</dd></div><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-[#aaa0b5]"><Users size={15} className="text-[#6de3ff]" /> Popularity</dt><dd className="font-bold">#2</dd></div><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-[#aaa0b5]"><Heart size={15} className="text-[#a98cff]" /> Favorites</dt><dd className="font-bold">31,204</dd></div></dl></section></aside>;
}

export function ChromaticDirection11() {
  const [compactSidebar, setCompactSidebar] = useState(true);
  return (
    <main className="min-h-screen bg-[#0c0813] text-[#fff8f4]">
      <div className={`mx-auto grid min-h-screen max-w-[1680px] transition-[grid-template-columns] duration-200 motion-reduce:transition-none ${compactSidebar ? 'lg:grid-cols-[4.75rem_minmax(0,1fr)]' : 'lg:grid-cols-[13.5rem_minmax(0,1fr)]'}`}>
        <Sidebar compact={compactSidebar} onToggle={() => setCompactSidebar(value => !value)} />
        <div className="min-w-0">
          <Header />
          <div className="border-b border-[#a98cff]/18 px-4 py-4 sm:px-7 xl:px-9"><DirectionSwitcher /></div>
          <Hero />
          <div className="px-4 pb-16 sm:px-7 xl:px-9">
            <DetailNavigation />
            <div id="aurora-overview" className="grid gap-x-9 xl:grid-cols-[minmax(0,1fr)_20rem]">
              <div className="min-w-0 divide-y divide-[#a98cff]/18"><Providers /><Franchise /><Characters /></div>
              <SideFacts />
            </div>
            <div className="flex items-center gap-2 border-t border-[#a98cff]/18 py-6 text-sm text-[#aaa0b5]"><Sparkles size={16} className="text-[#b7f26c]" /> Your archive is current across connected services.</div>
          </div>
        </div>
      </div>
    </main>
  );
}
