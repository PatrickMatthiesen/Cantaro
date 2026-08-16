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

const navigation = [
  { label: 'Home', icon: Home },
  { label: 'Library', icon: Library, active: true },
  { label: 'Calendar', icon: CalendarDays },
  { label: 'Discover', icon: Compass },
  { label: 'Sync center', icon: RefreshCw },
];

const tabs = ['Overview', 'Episodes', 'Progress', 'Providers', 'Franchise', 'Characters', 'Details'];

const franchise = [
  { title: 'Beyond Journey’s End', relation: 'Main story · watching', cover: mainCover },
  { title: 'Season 2', relation: 'Sequel · next in order', cover: secondCover },
  { title: 'Mini Anime', relation: 'Side story · 12 shorts', cover: miniCover },
];

function DirectionSwitcher() {
  return (
    <nav aria-label="Design directions" className="flex flex-wrap items-center gap-1">
      <span className="mr-3 text-xs font-semibold text-[#8da99d]">Direction</span>
      {Array.from({ length: 13 }, (_, index) => index + 1).map((number) => (
        <a
          key={number}
          href={`/${number}`}
          aria-current={number === 12 ? 'page' : undefined}
          className={`inline-flex min-h-9 min-w-9 items-center justify-center px-3 text-sm font-bold outline-none transition-colors focus-visible:ring-2 focus-visible:ring-[#a78bfa] ${number === 12 ? 'bg-[#a78bfa] text-[#100b1d]' : 'text-[#8da99d] hover:bg-[#fb7185]/12 hover:text-[#fda4af]'}`}
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
      <summary aria-label="Open account menu for Kael Ardent" className="flex size-10 cursor-pointer list-none items-center justify-center rounded-full bg-[#a78bfa] text-sm font-black text-[#100b1d] outline-none ring-2 ring-transparent transition-colors hover:bg-[#c4b5fd] focus-visible:ring-[#b9f66a] [&::-webkit-details-marker]:hidden">K</summary>
      <div className="absolute right-0 z-30 mt-3 w-64 border border-[#28463c] bg-[#0b1916] p-2">
        <div className="px-3 py-3"><p className="font-bold text-[#edf7f2]">Kael Ardent</p><p className="mt-1 text-xs text-[#8da99d]">Personal archive</p></div>
        <div className="border-t border-[#28463c] pt-2"><a href="/settings" className="flex min-h-10 items-center gap-3 px-3 text-sm text-[#b9ccc4] hover:bg-[#a78bfa]/10 hover:text-[#c4b5fd]"><Settings2 size={16} /> Settings</a><button type="button" className="flex min-h-10 w-full items-center px-3 text-left text-sm text-[#b9ccc4] hover:bg-[#fb7185]/10 hover:text-[#fda4af]">Log out</button></div>
      </div>
    </details>
  );
}

function Sidebar({ compact, onToggle }: { compact: boolean; onToggle: () => void }) {
  return (
    <aside className={`sticky top-0 hidden h-screen overflow-y-auto border-r border-[#19342c] bg-[#071411] px-4 py-6 lg:flex lg:flex-col ${compact ? 'items-center' : ''}`}>
      <a href="/" aria-label="Cantaro home" className={`flex items-center font-black text-[#edf7f2] ${compact ? 'justify-center' : 'gap-3 text-xl'}`}><span className="inline-flex size-9 items-center justify-center bg-[#a78bfa] text-sm text-[#100b1d]">C</span>{compact ? null : 'Cantaro'}</a>
      <nav aria-label="Primary navigation" className="mt-12 w-full space-y-1">
        {navigation.map(({ label, icon: Icon, active }) => (
          <a key={label} href="/media/library" title={compact ? label : undefined} aria-label={compact ? label : undefined} aria-current={active ? 'page' : undefined} className={`flex min-h-11 items-center text-sm font-semibold transition-colors ${compact ? 'justify-center px-2' : 'gap-3 px-3'} ${active ? 'bg-[#a78bfa]/14 text-[#c4b5fd]' : 'text-[#8da99d] hover:bg-[#fb7185]/8 hover:text-[#fda4af]'}`}><Icon size={18} />{compact ? null : label}</a>
        ))}
      </nav>
      {!compact ? <div className="mt-10 w-full border-t border-[#19342c] pt-6"><p className="px-3 text-xs font-bold text-[#66877b]">Your library</p>{['Anime', 'TV series', 'Movies', 'Watch later', 'Favorites'].map((label) => <a key={label} href="/media/library" className="block min-h-10 px-3 py-2.5 text-sm text-[#8da99d] transition-colors hover:text-[#7dd3fc]">{label}</a>)}</div> : null}
      <button type="button" onClick={onToggle} aria-label={compact ? 'Expand sidebar' : 'Collapse sidebar'} className={`mt-auto flex min-h-11 w-full items-center border-t border-[#19342c] pt-4 text-sm font-semibold text-[#8da99d] transition-colors hover:text-[#c4b5fd] focus-visible:outline-2 focus-visible:outline-[#a78bfa] ${compact ? 'justify-center' : 'gap-3 px-3'}`}>{compact ? <PanelLeftOpen size={18} /> : <><PanelLeftClose size={18} /> Fold sidebar</>}</button>
    </aside>
  );
}

function Header() {
  return (
    <header className="sticky top-0 z-20 border-b border-[#19342c] bg-[#06110f]/95 px-4 py-3 backdrop-blur-md sm:px-7 xl:px-9">
      <div className="flex items-center gap-3">
        <a href="/" className="flex shrink-0 items-center gap-2 font-black text-[#edf7f2] lg:hidden"><span className="inline-flex size-8 items-center justify-center bg-[#a78bfa] text-xs text-[#100b1d]">C</span><span className="hidden sm:inline">Cantaro</span></a>
        <a href="/search" className="relative hidden min-w-0 max-w-2xl flex-1 items-center border border-[#28463c] bg-[#0b1916] text-sm text-[#8da99d] transition-colors hover:border-[#7dd3fc] hover:text-[#b9e8fb] md:flex"><Search className="ml-3 shrink-0 text-[#7dd3fc]" size={18} /><span className="truncate px-3 py-3">Search anime, series, movies…</span></a>
        <div className="ml-auto flex shrink-0 items-center gap-1 sm:gap-2"><a href="/search?group=all&preview=true" aria-label="Search Cantaro" className="inline-flex size-10 items-center justify-center text-[#8da99d] transition-colors hover:bg-[#7dd3fc]/10 hover:text-[#7dd3fc] md:hidden"><Search size={18} /></a><button type="button" aria-label="Notifications" className="inline-flex size-10 items-center justify-center text-[#8da99d] transition-colors hover:bg-[#fb7185]/10 hover:text-[#fda4af]"><Bell size={18} /></button><AccountMenu /></div>
      </div>
    </header>
  );
}

function PosterDialog({ dialogRef }: { dialogRef: React.RefObject<HTMLDialogElement | null> }) {
  return (
    <dialog ref={dialogRef} aria-label="Frieren poster" className="m-auto max-h-[92vh] max-w-[min(92vw,34rem)] border border-[#a78bfa]/45 bg-[#06110f] p-3 text-[#edf7f2] backdrop:bg-[#020806]/90">
      <div className="flex items-center justify-between border-b border-[#19342c] pb-2"><span className="pl-2 text-sm font-semibold text-[#b9ccc4]">Full poster</span><button type="button" onClick={() => dialogRef.current?.close()} aria-label="Close poster" className="inline-flex size-10 items-center justify-center text-[#8da99d] hover:bg-[#fb7185]/10 hover:text-[#fda4af]"><X size={20} /></button></div>
      <img src={mainCover} alt="Frieren: Beyond Journey’s End full poster" className="mt-3 max-h-[78vh] w-auto object-contain" />
    </dialog>
  );
}

function Hero() {
  const posterDialogRef = useRef<HTMLDialogElement | null>(null);
  const openPoster = () => posterDialogRef.current?.showModal();
  return (
    <section aria-labelledby="garden-title" className="relative isolate min-h-[31rem] overflow-hidden border-b border-[#28463c]">
      <div className="absolute inset-0 bg-cover bg-center" style={{ backgroundImage: `url(${banner})` }} />
      <div className="absolute inset-0 bg-linear-to-r from-[#06110f] via-[#06110f]/78 to-[#10251e]/28" />
      <div className="absolute inset-0 bg-linear-to-t from-[#06110f] via-transparent to-[#06110f]/30" />
      <button type="button" onClick={openPoster} aria-label="View full poster" className="absolute right-4 top-5 z-20 border border-[#b9e8fb]/35 bg-[#06110f]/85 px-3 py-2 text-xs font-semibold text-[#b9e8fb] md:hidden">View poster</button>
      <div className="relative z-10 flex min-h-[31rem] items-end gap-8 px-4 pb-8 pt-28 sm:px-7 xl:px-9">
        <button type="button" onClick={openPoster} aria-label="View full poster" className="group hidden w-48 shrink-0 border border-[#a78bfa]/35 bg-[#0b1916] p-2 text-left transition-colors hover:border-[#fb7185] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#b9f66a] md:block xl:w-56"><img src={mainCover} alt="Frieren: Beyond Journey’s End cover" className="h-auto w-full object-contain" /><span className="mt-2 flex items-center justify-between px-1 text-xs text-[#8da99d] group-hover:text-[#fda4af]">Open artwork <ExternalLink size={14} /></span></button>
        <div className="min-w-0 max-w-4xl pb-1">
          <p className="font-semibold text-[#b9f66a]">TV series · Fantasy · Adventure</p>
          <h1 id="garden-title" className="mt-3 text-4xl font-black leading-[1.03] tracking-[-0.04em] text-[#f4fbf7] sm:text-5xl xl:text-6xl">Frieren: Beyond Journey’s End</h1>
          <p className="mt-4 text-sm text-[#b9ccc4]">2023 · 28 episodes · 24 min · <span className="text-[#b9e8fb]">9.1 community score</span></p>
          <p className="mt-5 max-w-3xl text-base leading-7 text-[#d0dfd8]">After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.</p>
          <div className="mt-7 flex flex-wrap gap-3"><button type="button" className="inline-flex min-h-12 items-center gap-2 bg-[#a78bfa] px-5 font-bold text-[#100b1d] transition-colors hover:bg-[#fda4af] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#b9f66a]"><Play size={18} fill="currentColor" /> Play episode 23</button><button type="button" className="inline-flex min-h-12 items-center gap-2 border border-[#5b766c] bg-[#06110f]/55 px-4 font-semibold text-[#edf7f2] hover:border-[#7dd3fc] hover:text-[#b9e8fb]"><Plus size={18} /> Add to list</button><button type="button" aria-label="More title actions" className="inline-flex size-12 items-center justify-center text-[#b9ccc4] hover:bg-[#fb7185]/12 hover:text-[#fda4af]"><MoreHorizontal size={20} /></button></div>
        </div>
      </div>
      <PosterDialog dialogRef={posterDialogRef} />
    </section>
  );
}

function Progress() {
  return (
    <section aria-labelledby="garden-progress" className="grid gap-6 border-b border-[#28463c] py-7 lg:grid-cols-[minmax(13rem,0.55fr)_minmax(20rem,1.45fr)_minmax(13rem,0.55fr)] lg:items-center lg:divide-x lg:divide-[#28463c]">
      <div><h2 id="garden-progress" className="text-lg font-bold text-[#c4b5fd]">Your progress</h2><p className="mt-2 text-3xl font-black">22 <span className="text-lg text-[#66877b]">/ 28</span> <span className="ml-2 text-sm text-[#b9f66a]">79%</span></p></div>
      <div className="lg:px-7"><div className="grid grid-cols-[2.5rem_minmax(0,1fr)_2.5rem] items-center gap-3"><button type="button" aria-label="Decrease watched episodes" className="inline-flex size-10 items-center justify-center text-[#8da99d] hover:bg-[#fb7185]/10 hover:text-[#fda4af] focus-visible:outline-2 focus-visible:outline-[#a78bfa]"><Minus size={18} /></button><div className="h-2 bg-[#19342c]" role="progressbar" aria-label="22 of 28 episodes watched" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}><div className="h-full w-[78.57%] bg-[#b9f66a]" /></div><button type="button" aria-label="Increase watched episodes" className="inline-flex size-10 items-center justify-center text-[#8da99d] hover:bg-[#b9f66a]/10 hover:text-[#b9f66a] focus-visible:outline-2 focus-visible:outline-[#a78bfa]"><Plus size={18} /></button></div><div className="mx-[3.25rem] mt-2 flex justify-between text-xs text-[#8da99d]"><span>Episode 22 watched</span><span>6 remaining</span></div></div>
      <div className="flex items-center justify-between gap-5 lg:pl-7"><div><p className="text-sm text-[#8da99d]">Your score</p><p className="mt-1 flex items-center gap-2 font-bold"><Star size={17} fill="currentColor" className="text-[#fda4af]" /> 9 / 10</p></div><div className="text-right"><p className="flex items-center gap-2 text-sm font-semibold text-[#b9f66a]"><RefreshCw size={15} /> Current</p><p className="mt-1 text-xs text-[#8da99d]">AniList · 12m</p></div></div>
    </section>
  );
}

function DetailNavigation() {
  return (
    <div className="flex items-start justify-between gap-5 border-b border-[#28463c]"><nav aria-label="Title details" className="flex min-w-0 flex-1 gap-7 overflow-x-auto">{tabs.map((tab, index) => <a key={tab} href="#garden-overview" aria-current={index === 0 ? 'page' : undefined} className={`min-h-14 shrink-0 border-b py-5 text-sm font-semibold transition-colors ${index === 0 ? 'border-[#a78bfa] text-[#c4b5fd]' : 'border-transparent text-[#8da99d] hover:border-[#fb7185] hover:text-[#fda4af]'}`}>{tab}</a>)}</nav><details className="group relative shrink-0 py-2"><summary className="flex min-h-10 cursor-pointer list-none items-center gap-2 px-2 text-sm font-semibold text-[#8da99d] hover:text-[#b9e8fb] [&::-webkit-details-marker]:hidden"><Settings2 size={17} /> Manage <ChevronDown size={15} className="transition-transform group-open:rotate-180 motion-reduce:transition-none" /></summary><div className="absolute right-0 z-20 mt-2 w-[min(20rem,calc(100vw-2rem))] border border-[#28463c] bg-[#0b1916] p-5"><p className="font-semibold">Secondary controls</p><label className="mt-4 block text-sm text-[#8da99d]">Episode sort<select className="mt-2 min-h-10 w-full border border-[#28463c] bg-[#06110f] px-3 text-[#edf7f2]"><option>Newest first</option><option>Oldest first</option></select></label><label className="mt-4 flex min-h-9 items-center gap-3 text-sm text-[#b9ccc4]"><input type="checkbox" defaultChecked className="size-4 accent-[#a78bfa]" /> Hide watched episodes</label></div></details></div>
  );
}

function ProviderLinks() {
  const providers = [
    { name: 'Crunchyroll', detail: 'Subtitles and dub', href: 'https://www.crunchyroll.com/' },
    { name: 'Netflix', detail: 'Available in your region', href: 'https://www.netflix.com/' },
  ];
  return <section id="providers" aria-labelledby="garden-watch"><div className="flex items-baseline justify-between gap-4"><h2 id="garden-watch" className="text-xl font-bold">Where to watch</h2><a href="#providers" className="text-sm text-[#7dd3fc] hover:text-[#fda4af]">All destinations</a></div><ul className="mt-5 grid gap-x-8 sm:grid-cols-2">{providers.map((provider) => <li key={provider.name} className="border-t border-[#28463c]"><a href={provider.href} target="_blank" rel="noreferrer" className="group flex min-h-20 items-center gap-3 py-4"><span className="inline-flex size-10 shrink-0 items-center justify-center bg-[#7dd3fc]/10 font-black text-[#7dd3fc]">{provider.name[0]}</span><span className="min-w-0 flex-1"><strong className="block transition-colors group-hover:text-[#fda4af]">{provider.name}</strong><span className="mt-1 block text-xs text-[#8da99d]">{provider.detail}</span></span><ExternalLink size={16} className="shrink-0 text-[#7dd3fc] transition-colors group-hover:text-[#fda4af]" /></a></li>)}</ul></section>;
}

function Franchise() {
  return <section id="franchise" aria-labelledby="garden-franchise"><div className="flex items-baseline justify-between gap-4"><h2 id="garden-franchise" className="text-xl font-bold">Franchise order</h2><a href="#franchise" className="text-sm text-[#7dd3fc] hover:text-[#fda4af]">View full franchise</a></div><ol className="mt-5 flex gap-5 overflow-x-auto pb-3">{franchise.map((item, index) => <li key={item.title} className="group grid w-72 shrink-0 grid-cols-[6rem_minmax(0,1fr)] gap-4 border-t border-[#28463c] pt-4"><img src={item.cover} alt="" className="h-36 w-24 object-cover transition-opacity group-hover:opacity-80" /><span className="flex min-w-0 flex-col py-1"><span className="font-mono text-xs text-[#66877b]">0{index + 1}</span><strong className="mt-3 leading-5 transition-colors group-hover:text-[#fda4af]">{item.title}</strong><span className="mt-1 text-xs text-[#8da99d]">{item.relation}</span><ChevronRight size={17} className="mt-auto text-[#7dd3fc]" /></span></li>)}</ol></section>;
}

function Characters() {
  return <section id="characters" aria-labelledby="garden-characters"><div className="flex items-baseline justify-between"><h2 id="garden-characters" className="text-xl font-bold">Main characters</h2><a href="#characters" className="text-sm text-[#7dd3fc] hover:text-[#fda4af]">See all</a></div><ul className="mt-5 grid gap-x-6 sm:grid-cols-2 xl:grid-cols-3">{[['Frieren', 'Main · Mage'], ['Fern', 'Main · Mage'], ['Stark', 'Main · Warrior'], ['Himmel', 'Supporting · Hero'], ['Heiter', 'Supporting · Priest'], ['Eisen', 'Supporting · Warrior']].map(([name, role], index) => <li key={name} className="group flex items-center gap-3 border-t border-[#28463c] py-4"><span className={`inline-flex size-10 items-center justify-center text-sm font-black ${index % 3 === 0 ? 'bg-[#a78bfa]/12 text-[#c4b5fd]' : index % 3 === 1 ? 'bg-[#7dd3fc]/10 text-[#b9e8fb]' : 'bg-[#fb7185]/10 text-[#fda4af]'}`}>{name.slice(0, 2).toUpperCase()}</span><span><strong className="block group-hover:text-[#fda4af]">{name}</strong><span className="text-xs text-[#8da99d]">{role}</span></span></li>)}</ul></section>;
}

function SideInformation() {
  return <aside className="border-t border-[#28463c] py-8 xl:border-l xl:border-t-0 xl:pl-8"><section aria-labelledby="garden-information"><h2 id="garden-information" className="text-xl font-bold text-[#c4b5fd]">Information</h2><dl className="mt-4 divide-y divide-[#19342c] text-sm">{[['Format', 'TV series'], ['Status', 'Finished'], ['Aired', 'Sep 2023 – Mar 2024'], ['Studio', 'Madhouse'], ['Source', 'Manga'], ['Genres', 'Adventure, Fantasy']].map(([term, value]) => <div key={term} className="grid grid-cols-[5.25rem_1fr] gap-3 py-3"><dt className="text-[#66877b]">{term}</dt><dd className="font-medium text-[#d0dfd8]">{value}</dd></div>)}</dl></section><section aria-labelledby="garden-connections" className="mt-8 border-t border-[#28463c] pt-8"><h2 id="garden-connections" className="text-xl font-bold text-[#b9f66a]">Connections</h2><dl className="mt-4 divide-y divide-[#19342c] text-sm"><div className="flex justify-between gap-4 py-3"><dt className="text-[#8da99d]">AniList</dt><dd className="text-right text-[#b9f66a]">Progress current · 12m</dd></div><div className="flex justify-between gap-4 py-3"><dt className="text-[#8da99d]">AnimeSchedule</dt><dd className="text-right text-[#b9e8fb]">Release schedule</dd></div></dl></section><section aria-labelledby="garden-community" className="mt-8 border-t border-[#28463c] pt-8"><h2 id="garden-community" className="text-xl font-bold text-[#fda4af]">Community</h2><dl className="mt-5 space-y-4"><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-[#8da99d]"><Star size={15} /> Score</dt><dd className="font-bold">9.1 · 48k</dd></div><div className="flex justify-between"><dt className="flex items-center gap-2 text-sm text-[#8da99d]"><Users size={15} /> Popularity</dt><dd className="font-bold">#2</dd></div><div className="flex justify-between"><dt className="text-sm text-[#8da99d]">Favorites</dt><dd className="font-bold">31,204</dd></div></dl></section></aside>;
}

export function ChromaticDirection12() {
  const [compactSidebar, setCompactSidebar] = useState(false);
  return (
    <main className="min-h-screen bg-[#06110f] text-[#edf7f2]">
      <div className={`mx-auto grid min-h-screen max-w-[1680px] transition-[grid-template-columns] duration-200 motion-reduce:transition-none ${compactSidebar ? 'lg:grid-cols-[4.75rem_minmax(0,1fr)]' : 'lg:grid-cols-[13.5rem_minmax(0,1fr)]'}`}>
        <Sidebar compact={compactSidebar} onToggle={() => setCompactSidebar((value) => !value)} />
        <div className="min-w-0"><Header /><div className="px-4 pt-5 sm:px-7 xl:px-9"><div className="border-b border-[#19342c] pb-5"><DirectionSwitcher /></div></div><Hero /><div className="px-4 pb-16 sm:px-7 xl:px-9"><Progress /><DetailNavigation /><div id="garden-overview" className="grid gap-x-9 xl:grid-cols-[minmax(0,1fr)_19rem]"><div className="min-w-0 divide-y divide-[#28463c]"><div className="py-8"><ProviderLinks /></div><div className="py-8"><Franchise /></div><div className="py-8"><Characters /></div></div><SideInformation /></div></div></div>
      </div>
    </main>
  );
}
