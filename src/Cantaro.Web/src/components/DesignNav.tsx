// Navigation component for switching between designs
interface DesignNavProps {
  currentDesign?: string;
  style?: 'light' | 'dark';
}

export function DesignNav({ currentDesign, style = 'light' }: DesignNavProps) {
  const designs = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
  
  const navigate = (design: string) => {
    window.history.pushState({}, '', `/${design}`);
    window.location.reload();
  };

  const goHome = () => {
    window.history.pushState({}, '', '/');
    window.location.reload();
  };

  const goComponents = () => {
    window.history.pushState({}, '', '/components');
    window.location.reload();
  };

  const isDark = style === 'dark';
  
  return (
    <div className={`fixed bottom-6 left-1/2 z-50 -translate-x-1/2 ${
      isDark ? 'bg-black/80 text-white' : 'bg-white/80 text-black'
    } rounded-full border px-4 py-3 shadow-2xl backdrop-blur-md ${
      isDark ? 'border-white/10' : 'border-black/10'
    }`}>
      <div className="flex max-w-[calc(100vw-48px)] items-center gap-2 overflow-x-auto">
        <button
          onClick={goHome}
          className={`rounded-full px-4 py-2 text-xs font-semibold transition-colors ${
            !currentDesign
              ? isDark ? 'bg-white text-black' : 'bg-black text-white'
              : isDark ? 'hover:bg-white/10' : 'hover:bg-black/5'
          }`}
        >
          Home
        </button>
        <button
          onClick={goComponents}
          className={`shrink-0 rounded-full px-4 py-2 text-xs font-semibold transition-colors ${
            currentDesign === 'components'
              ? isDark ? 'bg-white text-black' : 'bg-black text-white'
              : isDark ? 'hover:bg-white/10' : 'hover:bg-black/5'
          }`}
        >
          Components
        </button>
        <div className={`h-4 w-px shrink-0 ${isDark ? 'bg-white/20' : 'bg-black/20'}`} />
        {designs.map((design) => (
          <button
            key={design}
            onClick={() => navigate(design)}
            className={`shrink-0 rounded-full px-4 py-2 text-xs font-semibold transition-colors ${
              currentDesign === design
                ? isDark ? 'bg-white text-black' : 'bg-black text-white'
                : isDark ? 'hover:bg-white/10' : 'hover:bg-black/5'
            }`}
          >
            Design {design}
          </button>
        ))}
      </div>
    </div>
  );
}
