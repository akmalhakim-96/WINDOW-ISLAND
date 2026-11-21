// Window Island landing page interactions
const $ = (s, el = document) => el.querySelector(s);
const $$ = (s, el = document) => [...el.querySelectorAll(s)];

// ---- Changelog data (edit here to publish a new update) ----
const CHANGELOG = [
  { v: "1.0.0", name: "Pelancaran", date: "2026-11-21", items: [
    ["new", "Notch & Dynamic Island: muzik, notifikasi, waktu solat, CPU/RAM & internet"],
    ["new", "Dock gaya Mac dengan pembesaran licin, app aktif & ganti taskbar"],
    ["new", "6 widget desktop: Hari Ini & Nota, Cuaca, Jam, Waktu Solat, Peringatan, Sistem"],
    ["new", "Waktu solat offline (kaedah JAKIM) dengan makluman di notch"],
    ["new", "Peringatan bermasa (cth. “Mesyuarat @3pm”) & lampu privasi kamera/mikrofon"],
    ["new", "4 susun atur & 6 tema warna; semakan kemas kini automatik"],
  ]},
];
const LABEL = { new: "BARU", imp: "BAIK PULIH", fix: "PEMBAIKAN" };
$("#log").innerHTML = CHANGELOG.map(e => `
  <article class="entry">
    <header><h3>v${e.v}${e.name ? " — " + e.name : ""}</h3>
    <time datetime="${e.date}">${new Date(e.date).toLocaleDateString("ms-MY", { day: "numeric", month: "long", year: "numeric" })}</time></header>
    <ul>${e.items.map(([t, s]) => `<li><span class="chip ${t}">${LABEL[t]}</span>${s}</li>`).join("")}</ul>
  </article>`).join("");

// ---- Mobile menu ----
$("#menuBtn").onclick = () => $("#navLinks").classList.toggle("open");
$$("#navLinks a").forEach(a => a.onclick = () => $("#navLinks").classList.remove("open"));

// ---- Live clock (top bar + analog widget) ----
const cv = $("#analog"), ctx = cv.getContext("2d");
function drawClock(d) {
  const r = 80; ctx.clearRect(0, 0, 160, 160); ctx.save(); ctx.translate(r, r);
  ctx.fillStyle = "#fff"; ctx.beginPath(); ctx.arc(0, 0, r - 2, 0, 7); ctx.fill();
  ctx.fillStyle = "#111"; ctx.font = "600 15px Inter"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
  for (let i = 1; i <= 12; i++) { const a = i * Math.PI / 6; ctx.fillText(i, Math.sin(a) * 60, -Math.cos(a) * 60); }
  const hand = (a, len, w, c) => { ctx.strokeStyle = c; ctx.lineWidth = w; ctx.lineCap = "round"; ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(Math.sin(a) * len, -Math.cos(a) * len); ctx.stroke(); };
  const s = d.getSeconds(), m = d.getMinutes() + s / 60, h = d.getHours() % 12 + m / 60;
  hand(h * Math.PI / 6, 38, 5, "#111"); hand(m * Math.PI / 30, 56, 3.5, "#111"); hand(s * Math.PI / 30, 62, 1.5, "#f59e0b");
  ctx.fillStyle = "#f59e0b"; ctx.beginPath(); ctx.arc(0, 0, 4, 0, 7); ctx.fill(); ctx.restore();
}
function tick() {
  const d = new Date();
  $("#tbClock").textContent = d.toLocaleDateString("ms-MY", { weekday: "short" }) + " " + d.toLocaleTimeString("ms-MY", { hour: "2-digit", minute: "2-digit" });
  drawClock(d);
}
tick(); setInterval(tick, 1000);

// ---- Weather hours ----
const now = new Date().getHours(), icons = ["☀️", "⛅", "☀️", "🌤️", "⛈️", "🌙"];
$("#hours").innerHTML = icons.map((ic, i) => `<div>${String((now + i) % 24).padStart(2, "0")}<br>${ic}<br>${33 - i}°</div>`).join("");

// ---- Calendar ----
(function cal() {
  const d = new Date(), y = d.getFullYear(), m = d.getMonth();
  $("#calTitle").textContent = d.toLocaleDateString("ms-MY", { month: "long" });
  const first = (new Date(y, m, 1).getDay() + 6) % 7, days = new Date(y, m + 1, 0).getDate();
  let h = "ISRKJSA".split("").map(c => `<span class="d">${c}</span>`).join("");
  h += "<span></span>".repeat(first);
  for (let i = 1; i <= days; i++) h += `<span class="${i === d.getDate() ? "t" : ""}">${i}</span>`;
  $("#calGrid").innerHTML = h;
})();

// ---- Music progress + Dynamic Island ----
let p = 0; setInterval(() => { p = (p + 0.5) % 100; $("#prog").style.width = p + "%"; }, 200);
const island = $("#island"), msgs = ["Halstage — terima kasih", "⏱ Pemasa 24:59", "⬇ Memuat turun 68%", "🔔 Mesyuarat 3:00 PTG"];
let mi = 0;
island.onclick = () => island.classList.toggle("big");
setInterval(() => { mi = (mi + 1) % msgs.length; $("#islandText").textContent = msgs[mi]; }, 3500);

// ---- Reminders counter ----
$$(".remind input").forEach(cb => cb.onchange = () => { $("#remCount").textContent = $$(".remind input:not(:checked)").length; });

// ---- Dock magnification ----
const dock = $("#dock"), icons2 = $$("span", dock);
dock.addEventListener("mousemove", e => icons2.forEach(ic => {
  const r = ic.getBoundingClientRect(), dist = Math.abs(e.clientX - (r.left + r.width / 2));
  ic.style.transform = `scale(${1 + Math.max(0, 0.6 - dist / 120)})`;
}));
dock.addEventListener("mouseleave", () => icons2.forEach(ic => ic.style.transform = ""));


$("#yr").textContent = new Date().getFullYear();
