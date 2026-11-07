// Source geometry is measured in the unmodified Figma illustration, 1584 × 993.
// This module only adds vector facial motion and CSS masks; no bitmap is edited.
export const MANGA_GEOMETRY = {
  width: 1584,
  height: 993,
  eyes: {
    left: {center: [767, 271], bounds: [735, 254, 798, 287], angle: -14},
    right: {center: [871, 226], bounds: [841, 205, 901, 244], angle: -18}
  },
  mouth: {center: [849, 322], bounds: [822, 307, 875, 337], angle: -18},
  regions: {
    head: {
      origin: [864, 395],
      polygon: [[725,34],[772,30],[797,20],[843,24],[890,39],[947,76],[980,127],[989,169],[1000,198],[999,226],[981,258],[989,280],[1013,289],[1010,309],[1029,324],[1037,353],[1030,387],[1019,401],[992,399],[948,392],[932,350],[917,330],[893,353],[877,367],[854,362],[826,347],[802,333],[776,316],[743,298],[735,292],[741,332],[756,376],[760,409],[776,444],[768,465],[743,437],[729,409],[714,389],[705,372],[697,363],[682,335],[670,306],[657,292],[644,260],[639,238],[642,196],[655,157],[669,120],[688,85],[705,56]]
    },
    cardHand: {
      origin: [600, 586],
      polygon: [[553,330],[657,309],[698,476],[638,499],[636,549],[621,578],[600,596],[576,588],[561,565],[556,541],[539,511],[536,481],[548,449],[553,419],[575,417]]
    },
    restingHand: {
      origin: [879, 809],
      polygon: [[780,764],[815,751],[847,755],[869,772],[879,795],[880,819],[865,828],[841,835],[824,841],[809,836],[791,838],[778,829],[765,837],[748,833],[750,821],[757,806]]
    }
  }
};

export function createMangaFace() {
  return `<defs>
    <linearGradient id="manga-left-lid" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#f7cab9"/><stop offset="1" stop-color="#f4c0ae"/></linearGradient>
    <linearGradient id="manga-right-lid" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#f3c7b4"/><stop offset="1" stop-color="#f3bdad"/></linearGradient>
    <radialGradient id="manga-mouth-skin"><stop stop-color="#f4c8b6"/><stop offset=".73" stop-color="#f4c8b6"/><stop offset="1" stop-color="#f4c8b6" stop-opacity="0"/></radialGradient>
  </defs>
  <g class="eye-lid manga-eye left" transform="rotate(-14 767 271)">
    <ellipse cx="767" cy="271" rx="35" ry="15" fill="url(#manga-left-lid)"/>
    <path d="M736 271Q766 282 797 271" fill="none" stroke="#373237" stroke-width="3" stroke-linecap="round"/>
    <path d="m737 272-5-2m7 3-4 2" fill="none" stroke="#373237" stroke-width="2" stroke-linecap="round"/>
  </g>
  <g class="eye-lid manga-eye right" transform="rotate(-18 871 226)">
    <ellipse cx="871" cy="226" rx="34" ry="16" fill="url(#manga-right-lid)"/>
    <path d="M841 226Q871 237 902 226" fill="none" stroke="#373237" stroke-width="3" stroke-linecap="round"/>
    <path d="m900 227 5-3m-4 5 4 1" fill="none" stroke="#373237" stroke-width="2" stroke-linecap="round"/>
  </g>
  <g class="mouth-cover manga-mouth" transform="rotate(-18 849 322)">
    <ellipse cx="849" cy="323" rx="34" ry="21" fill="url(#manga-mouth-skin)"/>
    <g class="mouth-moving">
      <path d="M824 315Q849 310 874 315Q865 336 848 335Q833 334 824 315Z" fill="#8d5351" stroke="#614344" stroke-width="1.8" stroke-linejoin="round"/>
      <path d="M827 315Q849 312 871 315L867 320Q849 321 830 319Z" fill="#fff1e1"/>
      <path d="M836 330Q850 322 863 329Q857 335 847 334Z" fill="#d68e86"/>
    </g>
  </g>`;
}

const percent = ([x, y]) => `${x / MANGA_GEOMETRY.width * 100}% ${y / MANGA_GEOMETRY.height * 100}%`;

export function setupMangaFace({overlay} = {}) {
  if (!overlay) return;
  overlay.setAttribute('viewBox', '0 0 1584 993');
  overlay.setAttribute('preserveAspectRatio', 'none');
  overlay.innerHTML = createMangaFace();
  const character = overlay.closest('#character');
  if (!character) return;
  const masks = {'.head-piece': 'head', '.left-arm': 'cardHand', '.right-arm': 'restingHand'};
  for (const [selector, name] of Object.entries(masks)) {
    const element = character.querySelector(selector);
    const region = MANGA_GEOMETRY.regions[name];
    if (!element) continue;
    element.style.clipPath = `polygon(${region.polygon.map(percent).join(',')})`;
    element.style.transformOrigin = percent(region.origin);
  }
  const head = character.querySelector('.head-group');
  if (head) head.style.transformOrigin = percent(MANGA_GEOMETRY.regions.head.origin);
}
