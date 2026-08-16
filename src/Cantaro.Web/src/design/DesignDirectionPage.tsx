import {
  ArrowRight,
  Bell,
  BookOpen,
  CalendarDays,
  Check,
  ChevronRight,
  ChevronDown,
  CirclePlay,
  Compass,
  Grid2X2,
  Home,
  Library,
  ListFilter,
  Minus,
  MoreHorizontal,
  Play,
  Plus,
  RefreshCw,
  Search,
  Settings2,
  Sparkles,
  Star,
  Users,
} from 'lucide-react';
import { RefinedArchiveDirection } from './RefinedArchiveDirections';

const coverUrl =
  'https://s4.anilist.co/file/anilistcdn/media/anime/cover/large/bx154587-qQTzQnEJJ3oB.jpg';

const libraryTitles = [
  { title: 'Frieren: Beyond Journey’s End', meta: '22 of 28 episodes', year: '2023', status: 'Watching' },
  { title: 'Delicious in Dungeon', meta: '24 of 24 episodes', year: '2024', status: 'Completed' },
  { title: 'The Apothecary Diaries', meta: '18 of 24 episodes', year: '2023', status: 'Watching' },
  { title: 'Pluto', meta: '8 of 8 episodes', year: '2023', status: 'Completed' },
];

const directionNames = ['Editorial ledger', 'Cinematic index', 'Quiet library', 'Dense signal', 'Expressive gallery', 'Midnight archive', 'Complete archive', 'Balanced archive', 'Cinematic dossier', 'Compact index', 'Aurora archive', 'Midnight garden', 'Signal cinema'];

type DesignDirectionPageProps = {
  designId: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10;
};

function DirectionSwitcher({ direction, dark = false }: { direction: number; dark?: boolean }) {
  return (
    <nav aria-label="Design directions" className="flex flex-wrap items-center gap-x-1 gap-y-2">
      <span className={`mr-3 text-xs font-semibold ${dark ? 'text-white/55' : 'text-slate-500'}`}>Direction</span>
      {directionNames.map((name, index) => {
        const number = index + 1;
        const active = direction === number;
        return (
          <a
            key={name}
            href={`/${number}`}
            aria-current={active ? 'page' : undefined}
            aria-label={`${number}: ${name}`}
            className={`inline-flex min-h-9 min-w-9 items-center justify-center rounded-md px-3 text-sm font-semibold outline-none transition-colors focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:ring-offset-2 ${
              active
                ? dark
                  ? 'bg-white text-slate-950'
                  : 'bg-slate-950 text-white'
                : dark
                  ? 'text-white/60 hover:bg-white/10 hover:text-white'
                  : 'text-slate-500 hover:bg-slate-100 hover:text-slate-950'
            }`}
          >
            {number}
          </a>
        );
      })}
    </nav>
  );
}

function Poster({ className }: { className: string }) {
  return <img src={coverUrl} alt="Frieren: Beyond Journey’s End cover" className={className} />;
}

function EditorialLedger() {
  return (
    <main className="min-h-screen bg-[#f4f1eb] text-[#17201b]">
      <header className="border-b border-[#17201b]/20 px-5 py-5 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[1440px] flex-wrap items-center justify-between gap-5">
          <a href="/" className="font-serif text-2xl font-bold tracking-tight">Cantaro</a>
          <DirectionSwitcher direction={1} />
        </div>
      </header>

      <div className="mx-auto max-w-[1440px] px-5 py-8 sm:px-8 lg:px-12 lg:py-12">
        <div className="flex items-end justify-between border-b-2 border-[#17201b] pb-5">
          <div>
            <p className="mb-2 text-sm font-semibold text-[#93633b]">Your media archive</p>
            <h1 className="font-serif text-4xl font-bold tracking-[-0.03em] sm:text-5xl">Watching now</h1>
          </div>
          <button className="hidden min-h-11 items-center gap-2 border border-[#17201b] px-4 text-sm font-semibold hover:bg-[#17201b] hover:text-[#f4f1eb] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#93633b] sm:flex">
            <Search size={17} /> Search archive
          </button>
        </div>

        <section aria-labelledby="editorial-feature" className="grid gap-8 border-b border-[#17201b]/25 py-8 lg:grid-cols-[minmax(220px,0.8fr)_minmax(360px,1.2fr)_minmax(240px,0.7fr)] lg:gap-12">
          <Poster className="aspect-[3/4] w-full max-w-sm object-cover object-top" />
          <div className="self-center">
            <div className="mb-5 flex items-center gap-3 text-sm">
              <span className="font-semibold text-[#93633b]">Watching</span>
              <span aria-hidden="true">/</span>
              <span>22 of 28 episodes</span>
            </div>
            <h2 id="editorial-feature" className="max-w-2xl font-serif text-4xl font-bold leading-[1.04] tracking-[-0.03em] sm:text-6xl">
              Frieren: Beyond Journey’s End
            </h2>
            <p className="mt-6 max-w-xl text-base leading-7 text-[#435048]">
              After the hero’s party defeats the Demon King, an elven mage begins to understand the fleeting lives of the companions she left behind.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <button className="inline-flex min-h-11 items-center gap-2 bg-[#17201b] px-5 font-semibold text-white hover:bg-[#34433a] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#93633b]">
                <Play size={17} fill="currentColor" /> Continue episode 23
              </button>
              <button className="inline-flex min-h-11 items-center gap-2 border border-[#17201b] px-5 font-semibold hover:bg-white/50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#93633b]">
                <Plus size={17} /> Update progress
              </button>
            </div>
          </div>
          <dl className="divide-y divide-[#17201b]/20 border-y border-[#17201b]/20 self-end text-sm lg:self-center">
            {[
              ['Studio', 'Madhouse'],
              ['Released', 'Autumn 2023'],
              ['Format', 'TV · 28 episodes'],
              ['AniList', 'Synced 12 minutes ago'],
            ].map(([term, value]) => (
              <div key={term} className="grid grid-cols-[6rem_1fr] gap-4 py-4">
                <dt className="text-[#68726c]">{term}</dt>
                <dd className="font-semibold">{value}</dd>
              </div>
            ))}
          </dl>
        </section>

        <section aria-labelledby="ledger-library" className="py-8">
          <div className="mb-4 flex items-center justify-between">
            <h2 id="ledger-library" className="font-serif text-2xl font-bold">Recently in your library</h2>
            <a href="/media/library" className="inline-flex items-center gap-1 text-sm font-semibold underline decoration-[#93633b] decoration-2 underline-offset-4">View all <ArrowRight size={15} /></a>
          </div>
          <ol className="border-t border-[#17201b]">
            {libraryTitles.map((item, index) => (
              <li key={item.title} className="grid grid-cols-[2rem_1fr] gap-4 border-b border-[#17201b]/20 py-4 sm:grid-cols-[3rem_minmax(0,1fr)_9rem_7rem_1.5rem] sm:items-center">
                <span className="font-mono text-xs text-[#68726c]">0{index + 1}</span>
                <span className="font-serif text-lg font-semibold">{item.title}</span>
                <span className="hidden text-sm text-[#68726c] sm:block">{item.meta}</span>
                <span className="hidden text-sm font-semibold sm:block">{item.status}</span>
                <ChevronRight className="hidden sm:block" size={18} />
              </li>
            ))}
          </ol>
        </section>
      </div>
    </main>
  );
}

function CinematicIndex() {
  return (
    <main className="min-h-screen bg-[#07090d] text-white">
      <header className="border-b border-white/15 px-5 py-4 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[1500px] flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-8"><a href="/" className="text-lg font-black tracking-tight">CANTARO</a><span className="hidden text-sm text-white/50 md:block">Personal media index</span></div>
          <DirectionSwitcher direction={2} dark />
        </div>
      </header>

      <section aria-labelledby="cinematic-feature" className="relative isolate min-h-[700px] overflow-hidden">
        <Poster className="absolute inset-0 -z-20 h-full w-full object-cover object-[center_28%] opacity-65 md:object-[70%_22%]" />
        <div className="absolute inset-0 -z-10 bg-[linear-gradient(90deg,rgba(7,9,13,0.98)_0%,rgba(7,9,13,0.83)_40%,rgba(7,9,13,0.16)_78%),linear-gradient(0deg,rgba(7,9,13,1)_0%,transparent_55%)]" />
        <div className="mx-auto flex min-h-[700px] max-w-[1500px] items-end px-5 py-10 sm:px-8 lg:px-12 lg:py-16">
          <div className="w-full max-w-3xl">
            <div className="mb-7 flex items-center gap-4 text-sm text-white/70"><span className="text-[#f4b860]">Now watching</span><span>Episode 22 complete</span></div>
            <h1 id="cinematic-feature" className="text-5xl font-black leading-[0.96] tracking-[-0.04em] sm:text-7xl lg:text-8xl">Frieren: Beyond Journey’s End</h1>
            <p className="mt-6 max-w-2xl text-base leading-7 text-white/72 sm:text-lg">An immortal mage retraces a decade-long journey and discovers how much the brief lives around her changed everything.</p>
            <div className="mt-8 h-1 w-full max-w-md bg-white/20" aria-label="22 of 28 episodes watched" role="progressbar" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}>
              <div className="h-full w-[78.57%] bg-[#f4b860]" />
            </div>
            <div className="mt-3 flex max-w-md justify-between text-xs text-white/55"><span>22 watched</span><span>6 remaining</span></div>
            <div className="mt-9 flex flex-wrap gap-3">
              <button className="inline-flex min-h-12 items-center gap-2 bg-[#f4b860] px-6 font-bold text-[#171006] hover:bg-[#ffd28c] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"><Play size={18} fill="currentColor" /> Play episode 23</button>
              <button className="inline-flex min-h-12 items-center gap-2 border border-white/40 bg-black/30 px-5 font-semibold hover:bg-white hover:text-black focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"><Plus size={18} /> Update</button>
            </div>
            <dl className="mt-12 flex flex-wrap gap-x-10 gap-y-5 border-t border-white/20 pt-5 text-sm">
              <div><dt className="text-white/45">Studio</dt><dd className="mt-1 font-semibold">Madhouse</dd></div>
              <div><dt className="text-white/45">Season</dt><dd className="mt-1 font-semibold">Autumn 2023</dd></div>
              <div><dt className="text-white/45">Provider</dt><dd className="mt-1 flex items-center gap-1 font-semibold"><Check size={14} className="text-[#f4b860]" /> AniList synced</dd></div>
            </dl>
          </div>
        </div>
      </section>

      <section aria-labelledby="cinematic-library" className="mx-auto max-w-[1500px] px-5 pb-14 sm:px-8 lg:px-12">
        <div className="mb-5 flex items-center justify-between border-b border-white/20 pb-4"><h2 id="cinematic-library" className="text-xl font-bold">Continue through the archive</h2><a href="/media/library" className="text-sm text-white/60 hover:text-white">Full library →</a></div>
        <div className="grid divide-y divide-white/10 lg:grid-cols-2 lg:gap-x-12 lg:divide-y-0">
          {libraryTitles.map((item, index) => (
            <a key={item.title} href="/media/library" className="group flex items-center gap-4 border-b border-white/10 py-5 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-[#f4b860]">
              <span className="w-8 font-mono text-xs text-white/35">0{index + 1}</span><span className="min-w-0 flex-1"><span className="block truncate font-semibold group-hover:text-[#f4b860]">{item.title}</span><span className="mt-1 block text-sm text-white/45">{item.meta}</span></span><ChevronRight size={18} className="text-white/35" />
            </a>
          ))}
        </div>
      </section>
    </main>
  );
}

function QuietLibrary() {
  return (
    <main className="min-h-screen bg-white text-slate-900">
      <header className="border-b border-slate-200 px-5 py-4 sm:px-8">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center justify-between gap-4">
          <a href="/" className="flex items-center gap-2 font-bold"><Library size={20} className="text-violet-700" /> Cantaro</a>
          <DirectionSwitcher direction={3} />
        </div>
      </header>
      <div className="mx-auto grid max-w-7xl lg:grid-cols-[12rem_1fr]">
        <nav aria-label="Media navigation" className="hidden border-r border-slate-200 px-5 py-9 lg:block">
          <p className="mb-3 text-xs font-semibold text-slate-500">Media</p>
          <a href="/media/library" aria-current="page" className="block border-l-2 border-violet-700 py-2 pl-3 text-sm font-semibold text-violet-800">Library</a>
          <a href="/media/review" className="block py-2 pl-[14px] text-sm text-slate-600 hover:text-slate-950">Review</a>
          <a href="/media/providers" className="block py-2 pl-[14px] text-sm text-slate-600 hover:text-slate-950">Providers</a>
        </nav>
        <div className="min-w-0 px-5 py-8 sm:px-8 lg:px-10 lg:py-10">
          <div className="mb-8 flex flex-wrap items-center justify-between gap-4">
            <div><p className="text-sm text-slate-500">Media library</p><h1 className="mt-1 text-3xl font-bold tracking-tight">Watching</h1></div>
            <div className="flex gap-2"><button aria-label="Search library" className="inline-flex size-10 items-center justify-center border border-slate-300 hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600"><Search size={18} /></button><button className="inline-flex min-h-10 items-center gap-2 bg-violet-700 px-4 text-sm font-semibold text-white hover:bg-violet-800 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600"><Plus size={17} /> Add media</button></div>
          </div>

          <section aria-labelledby="quiet-feature" className="grid gap-7 border-y border-slate-200 py-7 sm:grid-cols-[150px_1fr] lg:grid-cols-[190px_minmax(0,1fr)_13rem]">
            <Poster className="aspect-[3/4] w-full object-cover object-top" />
            <div className="self-center">
              <p className="text-sm font-medium text-violet-700">Watching · 22 of 28</p>
              <h2 id="quiet-feature" className="mt-2 text-2xl font-bold tracking-tight sm:text-3xl">Frieren: Beyond Journey’s End</h2>
              <p className="mt-3 max-w-xl text-sm leading-6 text-slate-600">After the hero’s party defeats the Demon King, Frieren begins a quieter journey to understand the people who travelled beside her.</p>
              <div className="mt-5 flex flex-wrap gap-3"><button className="inline-flex min-h-10 items-center gap-2 bg-slate-900 px-4 text-sm font-semibold text-white hover:bg-slate-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-violet-600"><Play size={16} fill="currentColor" /> Continue</button><button className="min-h-10 px-3 text-sm font-semibold text-slate-600 underline underline-offset-4 hover:text-slate-950">Edit progress</button></div>
            </div>
            <dl className="grid grid-cols-2 gap-x-4 gap-y-5 border-t border-slate-200 pt-5 text-sm sm:col-start-2 lg:col-auto lg:grid-cols-1 lg:border-l lg:border-t-0 lg:pl-6 lg:pt-0">
              <div><dt className="text-slate-500">Next episode</dt><dd className="mt-1 font-semibold">Episode 23</dd></div><div><dt className="text-slate-500">Studio</dt><dd className="mt-1 font-semibold">Madhouse</dd></div><div><dt className="text-slate-500">AniList</dt><dd className="mt-1 flex items-center gap-1 font-semibold text-emerald-700"><Check size={14} /> Synced</dd></div>
            </dl>
          </section>

          <section aria-labelledby="quiet-list" className="py-8">
            <div className="mb-3 flex items-center justify-between"><h2 id="quiet-list" className="text-lg font-bold">Recently updated</h2><button className="inline-flex items-center gap-2 text-sm text-slate-600 hover:text-slate-950"><ListFilter size={16} /> Filter</button></div>
            <ul className="divide-y divide-slate-200">
              {libraryTitles.map((item) => (
                <li key={item.title} className="flex items-center gap-4 py-4"><div className="min-w-0 flex-1"><p className="truncate font-semibold">{item.title}</p><p className="mt-1 text-sm text-slate-500">{item.year} · {item.meta}</p></div><span className="hidden text-sm text-slate-600 sm:block">{item.status}</span><button aria-label={`More options for ${item.title}`} className="inline-flex size-9 items-center justify-center text-slate-500 hover:bg-slate-100 hover:text-slate-950"><MoreHorizontal size={18} /></button></li>
              ))}
            </ul>
          </section>
        </div>
      </div>
    </main>
  );
}

function DenseSignal() {
  return (
    <main className="min-h-screen bg-[#edf0ec] font-mono text-[#17231e]">
      <header className="border-b-2 border-[#17231e] bg-[#dce5dd] px-4 py-3 sm:px-6">
        <div className="mx-auto flex max-w-[1500px] flex-wrap items-center justify-between gap-3"><a href="/" className="flex items-center gap-2 text-sm font-bold"><span className="inline-flex size-7 items-center justify-center bg-[#17231e] text-[#b9ff62]">C</span> CANTARO / MEDIA</a><DirectionSwitcher direction={4} /></div>
      </header>
      <div className="mx-auto max-w-[1500px] px-4 py-5 sm:px-6">
        <div className="mb-5 flex flex-wrap items-end justify-between gap-4 border-b-2 border-[#17231e] pb-4">
          <div><p className="text-xs font-bold text-[#496057]">ARCHIVE VIEW</p><h1 className="mt-1 text-2xl font-black tracking-tight sm:text-3xl">ACTIVE MEDIA / 04</h1></div>
          <div className="flex gap-2"><button className="inline-flex min-h-10 items-center gap-2 border-2 border-[#17231e] px-3 text-xs font-bold hover:bg-[#17231e] hover:text-white"><Search size={15} /> SEARCH</button><button className="inline-flex min-h-10 items-center gap-2 bg-[#17231e] px-3 text-xs font-bold text-white hover:bg-[#30443b]"><Plus size={15} /> ADD ENTRY</button></div>
        </div>

        <section aria-labelledby="signal-feature" className="grid border-2 border-[#17231e] bg-white lg:grid-cols-[230px_minmax(0,1fr)_260px]">
          <Poster className="aspect-[3/4] h-full w-full object-cover object-top" />
          <div className="border-t-2 border-[#17231e] p-5 sm:p-7 lg:border-l-2 lg:border-t-0">
            <div className="mb-5 flex flex-wrap items-center gap-3 text-xs font-bold"><span className="bg-[#b9ff62] px-2 py-1">WATCHING</span><span>EP.22 / 28</span><span className="text-[#496057]">UPDATED TODAY</span></div>
            <h2 id="signal-feature" className="max-w-3xl font-sans text-3xl font-black leading-tight tracking-[-0.03em] sm:text-5xl">Frieren: Beyond Journey’s End</h2>
            <p className="mt-5 max-w-2xl font-sans text-sm leading-6 text-[#496057]">An elven mage returns to the roads she once travelled, measuring an immortal life against the memories of her companions.</p>
            <div className="mt-7 grid max-w-2xl grid-cols-[repeat(28,minmax(5px,1fr))] gap-1" role="img" aria-label="22 of 28 episodes watched">
              {Array.from({ length: 28 }, (_, index) => <span key={index} className={`h-3 ${index < 22 ? 'bg-[#17231e]' : 'bg-[#cad2cc]'}`} />)}
            </div>
            <div className="mt-7 flex flex-wrap gap-2"><button className="inline-flex min-h-11 items-center gap-2 bg-[#17231e] px-4 text-xs font-bold text-white hover:bg-[#30443b]"><CirclePlay size={17} /> CONTINUE E23</button><button className="min-h-11 border-2 border-[#17231e] px-4 text-xs font-bold hover:bg-[#dce5dd]">SET PROGRESS</button></div>
          </div>
          <dl className="grid grid-cols-2 border-t-2 border-[#17231e] text-xs lg:grid-cols-1 lg:border-l-2 lg:border-t-0">
            {[
              ['SOURCE', 'AniList'], ['SYNC', 'Current · 12m'], ['STUDIO', 'Madhouse'], ['SEASON', 'Autumn 2023'], ['FORMAT', 'TV / 28 EP'], ['SCORE', '9 / 10'],
            ].map(([term, value]) => <div key={term} className="border-b border-[#9eaaa2] p-4 last:border-b-0"><dt className="text-[#617069]">{term}</dt><dd className="mt-1 font-bold">{value}</dd></div>)}
          </dl>
        </section>

        <section aria-labelledby="signal-list" className="mt-6">
          <div className="grid grid-cols-[2rem_minmax(0,1fr)_6rem] gap-3 border-b-2 border-[#17231e] pb-2 text-[11px] font-bold sm:grid-cols-[2rem_minmax(0,1fr)_8rem_8rem_6rem]"><span>#</span><h2 id="signal-list">TITLE</h2><span className="hidden sm:block">PROGRESS</span><span className="hidden sm:block">STATE</span><span>YEAR</span></div>
          <ol>
            {libraryTitles.map((item, index) => <li key={item.title} className="grid grid-cols-[2rem_minmax(0,1fr)_6rem] gap-3 border-b border-[#9eaaa2] py-3 text-xs sm:grid-cols-[2rem_minmax(0,1fr)_8rem_8rem_6rem]"><span className="text-[#617069]">0{index + 1}</span><span className="truncate font-bold">{item.title}</span><span className="hidden sm:block">{item.meta.split(' episodes')[0]}</span><span className="hidden sm:block">{item.status}</span><span>{item.year}</span></li>)}
          </ol>
        </section>
      </div>
    </main>
  );
}

function ExpressiveGallery() {
  return (
    <main className="min-h-screen bg-[#f3edff] text-[#241432]">
      <header className="px-5 py-5 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[1440px] flex-wrap items-center justify-between gap-4"><a href="/" className="flex items-center gap-2 text-xl font-black tracking-tight"><Sparkles className="text-[#d64a63]" size={22} /> cantaro</a><DirectionSwitcher direction={5} /></div>
      </header>
      <div className="mx-auto max-w-[1440px] px-5 pb-14 sm:px-8 lg:px-12">
        <section aria-labelledby="gallery-feature" className="relative mt-3 overflow-hidden bg-[#382051] text-white">
          <div className="grid lg:min-h-[610px] lg:grid-cols-[minmax(0,0.88fr)_minmax(420px,1.12fr)]">
            <div className="relative z-10 flex flex-col justify-between p-6 sm:p-10 lg:p-14">
              <div className="flex items-center gap-3 text-sm font-semibold text-[#f3c0ca]"><BookOpen size={17} /><span>Currently watching · 22 / 28</span></div>
              <div className="py-14 lg:py-8">
                <h1 id="gallery-feature" className="max-w-2xl text-4xl font-black leading-[1.02] tracking-[-0.04em] sm:text-6xl lg:text-7xl">Frieren: Beyond Journey’s End</h1>
                <p className="mt-6 max-w-xl text-base leading-7 text-[#ddd0e8]">Some journeys become more important after they end. Return to Frieren’s quiet search for the memories she did not know she was making.</p>
                <div className="mt-8 flex flex-wrap gap-3"><button className="inline-flex min-h-12 items-center gap-2 bg-[#f2d45c] px-5 font-bold text-[#241432] hover:bg-[#ffe77c] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"><Play size={18} fill="currentColor" /> Watch episode 23</button><button className="inline-flex min-h-12 items-center gap-2 px-4 font-semibold text-white underline decoration-[#d64a63] decoration-2 underline-offset-4 hover:text-[#f3c0ca]"><Plus size={18} /> Update progress</button></div>
              </div>
              <div className="flex flex-wrap gap-x-8 gap-y-3 border-t border-white/20 pt-5 text-sm text-[#ddd0e8]"><span>Madhouse</span><span>Autumn 2023</span><span className="flex items-center gap-1"><Check size={14} className="text-[#f2d45c]" /> AniList synced</span></div>
            </div>
            <div className="relative min-h-[430px] overflow-hidden lg:min-h-full"><Poster className="absolute inset-0 h-full w-full object-cover object-top" /><div className="absolute inset-0 bg-[linear-gradient(90deg,#382051_0%,transparent_24%),linear-gradient(0deg,rgba(56,32,81,0.55)_0%,transparent_35%)]" /><div className="absolute bottom-6 right-6 bg-[#d64a63] px-4 py-3 text-sm font-bold text-white">Next up · E23</div></div>
          </div>
        </section>

        <section aria-labelledby="gallery-library" className="pt-10">
          <div className="mb-6 flex flex-wrap items-end justify-between gap-4"><div><p className="mb-1 text-sm font-semibold text-[#a52f4b]">Your collection, still unfolding</p><h2 id="gallery-library" className="text-3xl font-black tracking-tight">Back into the story</h2></div><div className="flex gap-2"><button aria-label="Grid view" className="inline-flex size-10 items-center justify-center bg-[#382051] text-white"><Grid2X2 size={18} /></button><button aria-label="Filter library" className="inline-flex size-10 items-center justify-center border border-[#382051]/30 hover:bg-white"><ListFilter size={18} /></button></div></div>
          <div className="grid gap-x-8 border-t-2 border-[#382051] md:grid-cols-2">
            {libraryTitles.map((item, index) => <a key={item.title} href="/media/library" className="group grid grid-cols-[2.5rem_1fr_auto] items-center gap-3 border-b border-[#382051]/25 py-5 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#d64a63]"><span className="font-mono text-xs font-bold text-[#a52f4b]">0{index + 1}</span><span><span className="block font-bold group-hover:text-[#a52f4b]">{item.title}</span><span className="mt-1 block text-sm text-[#705b79]">{item.meta} · {item.year}</span></span><ArrowRight size={18} className="transition-transform group-hover:translate-x-1" /></a>)}
          </div>
        </section>
      </div>
    </main>
  );
}

function MidnightArchive() {
  return (
    <main className="min-h-screen bg-[#090b10] text-[#f7f3ea]">
      <header className="border-b border-white/12 px-5 py-4 sm:px-8 lg:px-12">
        <div className="mx-auto flex max-w-[1440px] flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-8">
            <a href="/" className="text-lg font-black tracking-tight">CANTARO</a>
            <span className="hidden text-sm text-white/50 md:block">Your personal media archive</span>
          </div>
          <DirectionSwitcher direction={6} dark />
        </div>
      </header>

      <div className="mx-auto max-w-[1440px] px-5 pb-16 pt-8 sm:px-8 lg:px-12 lg:pt-12">
        <section aria-labelledby="midnight-feature" className="grid gap-8 border-b border-white/15 pb-10 lg:grid-cols-[minmax(240px,0.72fr)_minmax(0,1.28fr)] lg:gap-14 lg:pb-14">
          <div className="self-start bg-[#121722] p-3 sm:p-4">
            <Poster className="h-auto w-full object-contain" />
          </div>

          <div className="flex min-w-0 flex-col justify-center lg:py-5">
            <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
              <span className="font-semibold text-[#f1b85b]">Now watching</span>
              <span className="text-white/55">Episode 22 of 28</span>
            </div>
            <h1 id="midnight-feature" className="mt-5 max-w-4xl text-4xl font-black leading-[1.02] tracking-[-0.04em] sm:text-6xl lg:text-7xl">
              Frieren: Beyond Journey’s End
            </h1>
            <p className="mt-6 max-w-2xl text-base leading-7 text-white/68 sm:text-lg">
              An immortal mage retraces a decade-long journey and discovers how much the brief lives around her changed everything.
            </p>

            <div className="mt-8 max-w-2xl">
              <div className="mb-3 flex items-baseline justify-between gap-4 text-sm">
                <span className="font-semibold">22 episodes watched</span>
                <span className="text-white/50">6 remaining</span>
              </div>
              <div className="h-1.5 bg-white/15" aria-label="22 of 28 episodes watched" role="progressbar" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}>
                <div className="h-full w-[78.57%] bg-[#f1b85b]" />
              </div>
            </div>

            <div className="mt-8 flex flex-wrap items-center gap-3">
              <button className="inline-flex min-h-12 items-center gap-2 bg-[#f1b85b] px-5 font-bold text-[#181005] hover:bg-[#ffd48c] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white">
                <Play size={18} fill="currentColor" /> Play episode 23
              </button>
              <button className="inline-flex min-h-12 items-center gap-2 px-3 font-semibold text-white/78 underline decoration-white/35 underline-offset-4 hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#f1b85b]">
                <Plus size={18} /> Update progress
              </button>
            </div>

            <dl className="mt-10 grid gap-x-8 gap-y-5 border-t border-white/15 pt-5 text-sm sm:grid-cols-3">
              <div><dt className="text-white/45">Studio</dt><dd className="mt-1 font-semibold">Madhouse</dd></div>
              <div><dt className="text-white/45">Season</dt><dd className="mt-1 font-semibold">Autumn 2023</dd></div>
              <div><dt className="text-white/45">Provider sync</dt><dd className="mt-1 flex items-center gap-1.5 font-semibold"><Check size={15} className="text-[#f1b85b]" /> AniList current · 12m</dd></div>
            </dl>
          </div>
        </section>

        <section aria-labelledby="midnight-library" className="pt-9">
          <div className="flex flex-wrap items-start justify-between gap-5 border-b border-white/20 pb-5">
            <div>
              <h2 id="midnight-library" className="text-2xl font-bold tracking-tight">Continue through the archive</h2>
              <p className="mt-1 text-sm text-white/48">Four titles, ordered by recent activity</p>
            </div>
            <details className="group relative">
              <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 border border-white/25 px-4 text-sm font-semibold text-white/78 hover:border-white/50 hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#f1b85b] [&::-webkit-details-marker]:hidden">
                <ListFilter size={17} /> Filter <ChevronDown size={16} className="transition-transform group-open:rotate-180 motion-reduce:transition-none" />
              </summary>
              <div className="absolute right-0 z-10 mt-2 w-[min(20rem,calc(100vw-2.5rem))] border border-white/20 bg-[#151922] p-5 shadow-lg shadow-black/35">
                <fieldset>
                  <legend className="font-semibold">Show in library</legend>
                  <div className="mt-4 space-y-3 text-sm text-white/72">
                    {['Watching', 'Completed', 'Planning'].map((label, index) => (
                      <label key={label} className="flex min-h-8 cursor-pointer items-center gap-3">
                        <input type="checkbox" defaultChecked={index < 2} className="size-4 accent-[#f1b85b]" /> {label}
                      </label>
                    ))}
                  </div>
                </fieldset>
                <div className="mt-5 flex justify-between border-t border-white/15 pt-4 text-sm">
                  <button className="text-white/55 underline underline-offset-4 hover:text-white">Reset</button>
                  <button className="bg-[#f1b85b] px-4 py-2 font-bold text-[#181005] hover:bg-[#ffd48c]">Apply</button>
                </div>
              </div>
            </details>
          </div>

          <ol>
            {libraryTitles.map((item, index) => (
              <li key={item.title} className="grid grid-cols-[2rem_minmax(0,1fr)_auto] items-center gap-3 border-b border-white/10 py-5 sm:grid-cols-[3rem_minmax(0,1fr)_10rem_7rem_1.5rem]">
                <span className="font-mono text-xs text-white/32">0{index + 1}</span>
                <a href="/media/library" className="font-semibold hover:text-[#f1b85b] focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-[#f1b85b]">{item.title}</a>
                <span className="hidden text-sm text-white/48 sm:block">{item.meta}</span>
                <span className="hidden text-sm font-semibold text-white/72 sm:block">{item.status}</span>
                <ChevronRight size={18} className="text-white/35" />
              </li>
            ))}
          </ol>
        </section>
      </div>
    </main>
  );
}

const completeArchiveNav = [
  { label: 'Home', icon: Home },
  { label: 'Library', icon: Library, active: true },
  { label: 'Calendar', icon: CalendarDays },
  { label: 'Discover', icon: Compass },
  { label: 'Sync center', icon: RefreshCw },
];

const detailTabs = ['Overview', 'Episodes', 'Progress', 'Providers', 'Franchise', 'Characters', 'Details'];

function CompleteArchive() {
  return (
    <main className="min-h-screen bg-[#070a10] text-[#f6f3eb]">
      <div className="mx-auto grid min-h-screen max-w-[1680px] lg:grid-cols-[13.5rem_minmax(0,1fr)]">
        <aside className="hidden border-r border-white/12 px-5 py-6 lg:flex lg:flex-col">
          <a href="/" className="flex items-center gap-3 text-xl font-black tracking-tight">
            <span className="inline-flex size-8 items-center justify-center bg-[#f1b85b] text-sm text-[#171006]">C</span>
            Cantaro
          </a>
          <nav aria-label="Primary navigation" className="mt-12 space-y-1">
            {completeArchiveNav.map(({ label, icon: Icon, active }) => (
              <a
                key={label}
                href="/media/library"
                aria-current={active ? 'page' : undefined}
                className={`flex min-h-11 items-center gap-3 border-l-2 px-3 text-sm font-semibold transition-colors ${active ? 'border-[#f1b85b] bg-white/6 text-white' : 'border-transparent text-white/52 hover:text-white'}`}
              >
                <Icon size={18} /> {label}
              </a>
            ))}
          </nav>
          <div className="mt-10 border-t border-white/12 pt-6">
            <p className="px-3 text-xs font-semibold uppercase tracking-[0.15em] text-white/35">Your library</p>
            <div className="mt-3 space-y-1 text-sm text-white/52">
              {['Anime', 'TV series', 'Movies', 'Watch later', 'Favorites'].map((label) => (
                <a key={label} href="/media/library" className="block min-h-10 px-3 py-2.5 hover:text-white">{label}</a>
              ))}
            </div>
          </div>
          <div className="mt-auto border-t border-white/12 pt-5">
            <p className="font-semibold">Kael Ardent</p>
            <p className="mt-1 text-xs text-white/40">Personal archive</p>
          </div>
        </aside>

        <div className="min-w-0">
          <header className="border-b border-white/12 px-4 py-4 sm:px-7 xl:px-10">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <a href="/" className="flex items-center gap-2 font-black lg:hidden"><span className="inline-flex size-7 items-center justify-center bg-[#f1b85b] text-xs text-[#171006]">C</span> Cantaro</a>
              <label className="relative order-3 w-full sm:order-none sm:max-w-xl">
                <span className="sr-only">Search media</span>
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" size={18} />
                <input type="search" placeholder="Search anime, series, movies…" className="min-h-11 w-full border border-white/16 bg-white/[0.035] pl-10 pr-4 text-sm text-white outline-none placeholder:text-white/35 focus:border-[#f1b85b]" />
              </label>
              <div className="flex items-center gap-3">
                <button aria-label="Notifications" className="inline-flex size-10 items-center justify-center text-white/55 hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#f1b85b]"><Bell size={18} /></button>
                <span className="hidden items-center gap-2 border-l border-white/12 pl-4 text-sm sm:flex"><Check size={16} className="text-[#f1b85b]" /><span><span className="block font-semibold">Synced</span><span className="block text-xs text-white/38">12 minutes ago</span></span></span>
              </div>
            </div>
            <div className="mt-4 overflow-x-auto pb-1 lg:hidden">
              <nav aria-label="Mobile navigation" className="flex min-w-max gap-5 text-sm text-white/52">
                {completeArchiveNav.map(({ label, active }) => <a key={label} href="/media/library" className={active ? 'font-semibold text-[#f1b85b]' : 'hover:text-white'}>{label}</a>)}
              </nav>
            </div>
          </header>

          <div className="px-4 pb-16 pt-6 sm:px-7 xl:px-10">
            <div className="mb-6 border-b border-white/12 pb-5">
              <DirectionSwitcher direction={7} dark />
            </div>

            <section aria-labelledby="complete-title" className="grid gap-8 border-b border-white/15 pb-9 md:grid-cols-[12rem_minmax(0,1fr)] xl:grid-cols-[15rem_minmax(0,1fr)_19rem] xl:gap-10">
              <div className="mx-auto w-full max-w-56 self-start border border-white/14 bg-[#10141c] p-2 md:max-w-none">
                <Poster className="h-auto w-full object-contain" />
              </div>

              <div className="min-w-0 self-center">
                <p className="text-sm font-semibold text-[#f1b85b]">TV series · Fantasy · Adventure</p>
                <h1 id="complete-title" className="mt-3 text-4xl font-black leading-[1.02] tracking-[-0.04em] sm:text-5xl 2xl:text-6xl">Frieren: Beyond Journey’s End</h1>
                <p className="mt-4 text-sm text-white/48">2023 · 28 episodes · 24 min · 9.1 community score</p>
                <p className="mt-5 max-w-3xl text-base leading-7 text-white/68">After the hero’s party defeats the Demon King, an immortal elven mage retraces their journey and begins to understand the brief lives of the people who travelled beside her.</p>
                <div className="mt-7 flex flex-wrap gap-3">
                  <button className="inline-flex min-h-12 items-center gap-2 bg-[#f1b85b] px-5 font-bold text-[#181005] hover:bg-[#ffd48c] focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"><Play size={18} fill="currentColor" /> Play episode 23</button>
                  <button className="inline-flex min-h-12 items-center gap-2 border border-white/22 px-4 font-semibold text-white/78 hover:border-white/50 hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#f1b85b]"><Plus size={18} /> Add to list</button>
                  <button aria-label="More title actions" className="inline-flex size-12 items-center justify-center text-white/55 hover:text-white focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#f1b85b]"><MoreHorizontal size={20} /></button>
                </div>
              </div>

              <div className="border-t border-white/15 pt-6 md:col-start-2 xl:col-auto xl:border-l xl:border-t-0 xl:pl-8 xl:pt-1">
                <div className="flex items-end justify-between gap-4">
                  <div><p className="text-xs font-semibold uppercase tracking-[0.14em] text-white/40">Your progress</p><p className="mt-2 text-3xl font-black">22 <span className="text-lg text-white/35">/ 28</span></p></div>
                  <span className="text-sm font-semibold text-[#f1b85b]">79%</span>
                </div>
                <div className="mt-5 h-1.5 bg-white/14" aria-label="22 of 28 episodes watched" role="progressbar" aria-valuemin={0} aria-valuemax={28} aria-valuenow={22}><div className="h-full w-[78.57%] bg-[#f1b85b]" /></div>
                <div className="mt-3 flex justify-between text-xs text-white/38"><span>Episode 22 watched</span><span>6 remaining</span></div>
                <div className="mt-6 flex items-center justify-between border-y border-white/12 py-3">
                  <button aria-label="Decrease watched episodes" className="inline-flex size-10 items-center justify-center text-white/55 hover:text-white focus-visible:outline-2 focus-visible:outline-[#f1b85b]"><Minus size={18} /></button>
                  <span className="text-sm font-semibold">Set progress</span>
                  <button aria-label="Increase watched episodes" className="inline-flex size-10 items-center justify-center text-white/55 hover:text-white focus-visible:outline-2 focus-visible:outline-[#f1b85b]"><Plus size={18} /></button>
                </div>
                <div className="mt-5 flex items-center justify-between"><span className="text-sm text-white/48">Your score</span><span className="flex items-center gap-2 font-bold"><Star size={17} fill="currentColor" className="text-[#f1b85b]" /> 9 / 10</span></div>
                <div className="mt-4 flex items-center gap-2 text-xs text-white/45"><RefreshCw size={14} className="text-[#f1b85b]" /> AniList progress is current</div>
              </div>
            </section>

            <div className="flex items-start justify-between gap-5 border-b border-white/15">
              <nav aria-label="Title details" className="flex min-w-0 flex-1 gap-7 overflow-x-auto">
                {detailTabs.map((tab, index) => <a key={tab} href="#overview" aria-current={index === 0 ? 'page' : undefined} className={`min-h-14 shrink-0 border-b-2 py-5 text-sm font-semibold ${index === 0 ? 'border-[#f1b85b] text-white' : 'border-transparent text-white/45 hover:text-white'}`}>{tab}</a>)}
              </nav>
              <details className="group relative shrink-0 py-2">
                <summary className="flex min-h-10 cursor-pointer list-none items-center gap-2 px-2 text-sm font-semibold text-white/55 hover:text-white focus-visible:outline-2 focus-visible:outline-[#f1b85b] [&::-webkit-details-marker]:hidden"><Settings2 size={17} /> Manage <ChevronDown size={15} className="transition-transform group-open:rotate-180 motion-reduce:transition-none" /></summary>
                <div className="absolute right-0 z-20 mt-2 w-[min(20rem,calc(100vw-2rem))] border border-white/18 bg-[#11151d] p-5 shadow-xl shadow-black/45">
                  <p className="font-semibold">Secondary controls</p>
                  <label className="mt-4 block text-sm text-white/55">Episode sort<select className="mt-2 min-h-10 w-full border border-white/18 bg-[#090c12] px-3 text-white"><option>Newest first</option><option>Oldest first</option></select></label>
                  <label className="mt-4 flex min-h-9 items-center gap-3 text-sm text-white/68"><input type="checkbox" defaultChecked className="size-4 accent-[#f1b85b]" /> Hide watched episodes</label>
                  <button className="mt-5 min-h-10 w-full bg-[#f1b85b] px-4 text-sm font-bold text-[#181005]">Apply changes</button>
                </div>
              </details>
            </div>

            <div id="overview" className="grid gap-x-10 xl:grid-cols-[minmax(0,1fr)_19rem]">
              <div className="min-w-0 divide-y divide-white/12">
                <section aria-labelledby="watch-heading" className="py-8">
                  <div className="flex items-baseline justify-between gap-4"><h2 id="watch-heading" className="text-xl font-bold">Where to watch</h2><a href="#providers" className="text-sm text-[#f1b85b] hover:text-[#ffd48c]">All providers</a></div>
                  <ul className="mt-5 grid gap-x-8 sm:grid-cols-2 2xl:grid-cols-3">
                    {[['Crunchyroll', 'Streaming · Sub / Dub'], ['AniList', 'Library and progress sync'], ['AnimeSchedule', 'Release schedule']].map(([name, detail]) => <li key={name} className="flex items-center gap-3 border-t border-white/10 py-4"><span className="inline-flex size-9 shrink-0 items-center justify-center bg-white/8 text-sm font-black text-[#f1b85b]">{name[0]}</span><span className="min-w-0 flex-1"><span className="block font-semibold">{name}</span><span className="mt-0.5 block text-xs text-white/42">{detail}</span></span><Check size={16} className="text-[#f1b85b]" /></li>)}
                  </ul>
                </section>

                <section aria-labelledby="franchise-heading" className="py-8">
                  <div className="flex items-baseline justify-between gap-4"><h2 id="franchise-heading" className="text-xl font-bold">Franchise order</h2><a href="#franchise" className="text-sm text-[#f1b85b] hover:text-[#ffd48c]">View full franchise</a></div>
                  <ol className="mt-5 grid gap-x-8 sm:grid-cols-2">
                    {[['01', 'Frieren: Beyond Journey’s End', 'Watching · episode 22'], ['02', 'Frieren: Beyond Journey’s End — Part 2', 'Next in order'], ['03', 'Sousou no Frieren: Mini Anime', 'Optional · 12 shorts']].map(([number, title, state]) => <li key={number} className="grid grid-cols-[2rem_minmax(0,1fr)_auto] items-center gap-3 border-t border-white/10 py-4"><span className="font-mono text-xs text-white/28">{number}</span><span><span className="block font-semibold">{title}</span><span className="mt-1 block text-xs text-white/42">{state}</span></span><ChevronRight size={17} className="text-white/30" /></li>)}
                  </ol>
                </section>

                <section aria-labelledby="characters-heading" className="py-8">
                  <div className="flex items-baseline justify-between gap-4"><h2 id="characters-heading" className="text-xl font-bold">Main characters</h2><a href="#characters" className="text-sm text-[#f1b85b] hover:text-[#ffd48c]">See all</a></div>
                  <ul className="mt-5 grid gap-x-8 sm:grid-cols-2 2xl:grid-cols-3">
                    {[['Frieren', 'Main · Mage'], ['Fern', 'Main · Mage'], ['Stark', 'Main · Warrior'], ['Himmel', 'Supporting · Hero'], ['Heiter', 'Supporting · Priest'], ['Eisen', 'Supporting · Warrior']].map(([name, role]) => <li key={name} className="flex items-center gap-3 border-t border-white/10 py-4"><span className="inline-flex size-10 shrink-0 items-center justify-center border border-white/15 text-sm font-bold text-white/55">{name.slice(0, 2).toUpperCase()}</span><span><span className="block font-semibold">{name}</span><span className="mt-1 block text-xs text-white/42">{role}</span></span></li>)}
                  </ul>
                </section>
              </div>

              <aside className="border-t border-white/12 xl:border-l xl:border-t-0 xl:pl-8">
                <section aria-labelledby="information-heading" className="py-8">
                  <h2 id="information-heading" className="text-xl font-bold">Information</h2>
                  <dl className="mt-5 divide-y divide-white/10 text-sm">
                    {[['Format', 'TV series'], ['Status', 'Finished'], ['Aired', 'Sep 2023 – Mar 2024'], ['Studio', 'Madhouse'], ['Source', 'Manga'], ['Genres', 'Adventure, Fantasy']].map(([term, value]) => <div key={term} className="grid grid-cols-[5.5rem_1fr] gap-3 py-3"><dt className="text-white/40">{term}</dt><dd className="font-medium text-white/78">{value}</dd></div>)}
                  </dl>
                </section>
                <section aria-labelledby="community-heading" className="border-t border-white/12 py-8">
                  <h2 id="community-heading" className="text-xl font-bold">Community</h2>
                  <dl className="mt-5 space-y-5">
                    <div className="flex items-center justify-between"><dt className="flex items-center gap-2 text-sm text-white/45"><Star size={16} /> Score</dt><dd className="font-bold">9.1 <span className="text-xs font-normal text-white/35">· 48k ratings</span></dd></div>
                    <div className="flex items-center justify-between"><dt className="flex items-center gap-2 text-sm text-white/45"><Users size={16} /> Popularity</dt><dd className="font-bold">#2</dd></div>
                    <div className="flex items-center justify-between"><dt className="text-sm text-white/45">Favorites</dt><dd className="font-bold">31,204</dd></div>
                  </dl>
                </section>
              </aside>
            </div>
          </div>
        </div>
      </div>
    </main>
  );
}

export function DesignDirectionPage({ designId }: DesignDirectionPageProps) {
  if (designId === 1) return <EditorialLedger />;
  if (designId === 2) return <CinematicIndex />;
  if (designId === 3) return <QuietLibrary />;
  if (designId === 4) return <DenseSignal />;
  if (designId === 5) return <ExpressiveGallery />;
  if (designId === 6) return <MidnightArchive />;
  if (designId === 7) return <CompleteArchive />;
  return <RefinedArchiveDirection direction={designId} />;
}
