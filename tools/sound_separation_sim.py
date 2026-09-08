import math, random
SR = 44100
rng = random.Random(20260905)

def clamp(v, lo=-1.0, hi=1.0): return max(lo, min(hi, v))
def lerp(a,b,t): return a + (b-a)*t

def rising_blip(f0, f1, dur, noise_mix=0.0):
    n = int(SR*dur); buf=[0.0]*n; phase=0.0
    for i in range(n):
        t = i/n
        freq = lerp(f0,f1,t)
        phase += 2*math.pi*freq/SR
        env = math.sin(math.pi*t)
        sine = math.sin(phase)
        noise = (rng.random()*2-1) if noise_mix>0 else 0.0
        buf[i] = clamp((sine*(1-noise_mix)+noise*noise_mix)*env*0.5)
    return buf

def noise_burst(dur, decay, amp):
    n=int(SR*dur); buf=[0.0]*n
    for i in range(n):
        t=i/n
        buf[i]=clamp((rng.random()*2-1)*math.exp(-decay*t)*amp)
    return buf

def thud(f0,f1,dur):
    n=int(SR*dur); buf=[0.0]*n; phase=0.0
    for i in range(n):
        t=i/n
        phase += 2*math.pi*lerp(f0,f1,t)/SR
        buf[i]=clamp(math.sin(phase)*math.exp(-8*t)*0.7)
    return buf

def click(dur, decay, tone_freq, noise_mix=0.6):
    n=int(SR*dur); buf=[0.0]*n; phase=0.0
    for i in range(n):
        t=i/n
        env=math.exp(-decay*t)
        noise=rng.random()*2-1
        phase += 2*math.pi*tone_freq/SR
        tone=math.sin(phase)
        buf[i]=clamp((noise*noise_mix+tone*(1-noise_mix))*env*0.6)
    return buf

def zcr(x):
    if len(x)<2: return 0.0
    c=sum(1 for i in range(1,len(x)) if (x[i-1]<0) != (x[i]<0))
    return c/(len(x)-1)
def prof(x): 
    z=zcr(x); return (len(x)*1000.0/SR, z*SR/2.0, z)

def perfect_parry():
    clash = click(0.14,14,520,0.35)
    ring  = rising_blip(900,1750,0.14)
    return [clamp(clash[i]*0.7 + (ring[i] if i<len(ring) else 0.0)*0.6, -0.95, 0.95) for i in range(len(clash))]

sounds = {
 "Jumped":        rising_blip(500,900,0.12),
 "DoubleJumped":  rising_blip(1000,1600,0.10),
 "Dashed":        noise_burst(0.18,9,0.45),
 "WallJumped":    rising_blip(420,760,0.11,0.25),
 "Landed":        thud(110,50,0.16),
 "PlayerDamaged": click(0.09,26,220,0.45),
 "Parried":       click(0.07,30,320,0.55),
 "PerfectParried":perfect_parry(),
}

import sys
CAND = sys.argv[1] if len(sys.argv)>1 else "current"
if CAND in ("current","full"):
    sounds["EnemyDamaged"]=click(0.05,40,900,0.2)
else:
    d,dec,f,nm = [float(v) for v in CAND.split(",")]
    sounds["EnemyDamaged"]=click(d,dec,f,nm)

P={k:prof(v) for k,v in sounds.items()}
for k,(ms,c,n) in P.items(): print(f"  {k:<16}{ms:6.0f}ms  centroid {c:8.0f}Hz  noisiness {n:.3f}")
names=list(P); bad=0
for i in range(len(names)):
    for j in range(i+1,len(names)):
        a,b=P[names[i]],P[names[j]]
        ok = abs(a[0]-b[0])>25 or abs(a[1]-b[1])>250 or abs(a[2]-b[2])>0.05
        if not ok:
            bad+=1; print(f"  TOO SIMILAR: {names[i]} vs {names[j]}  dMs={abs(a[0]-b[0]):.0f} dC={abs(a[1]-b[1]):.0f} dN={abs(a[2]-b[2]):.3f}")
print(f"  => {len(names)} sounds, {bad} indistinguishable pair(s)")

# ---- the full Phase 2 palette, measured against the existing one ----------
if CAND == "full":
    def two_tone_bell(f0, f1, dur, ratio=1.5):
        n=int(SR*dur); buf=[0.0]*n; p1=p2=0.0
        for i in range(n):
            t=i/n; f=lerp(f0,f1,t)
            p1 += 2*math.pi*f/SR
            p2 += 2*math.pi*f*ratio/SR
            env = math.sin(math.pi*t)**0.7
            buf[i]=clamp((math.sin(p1)*0.62 + math.sin(p2)*0.38)*env*0.55)
        return buf
    def punch(f0,f1,dur,decay,noise_amt):
        n=int(SR*dur); buf=[0.0]*n; phase=0.0
        for i in range(n):
            t=i/n
            phase += 2*math.pi*lerp(f0,f1,t)/SR
            env=math.exp(-decay*t)
            buf[i]=clamp((math.sin(phase)*(1-noise_amt) + (rng.random()*2-1)*noise_amt)*env*0.8)
        return buf

    extra = {
      "TelegraphGold": two_tone_bell(1400,2200,0.16),
      "TelegraphRed":  rising_blip(160,75,0.20,0.35),
      "HitLight":      click(0.04,45,1100,0.25),
      "HitHeavy":      click(0.08,18,450,0.30),
      "HitCharged":    punch(120,60,0.12,16,0.30),
      "HitAbsorbed":   click(0.045,60,2400,0.05),
      "HeatSizzle":    noise_burst(0.09,22,0.34),
      "Heartbeat":     thud(60,42,0.13),
    }
    all_s = dict(sounds); all_s.pop("EnemyDamaged", None); all_s.update(extra)
    P2={k:prof(v) for k,v in all_s.items()}
    print("\n===== FULL PALETTE =====")
    for k,(ms,c,n) in sorted(P2.items(), key=lambda kv: kv[1][1]):
        print(f"  {k:<16}{ms:6.0f}ms  centroid {c:8.0f}Hz  noisiness {n:.3f}")
    names=list(P2); bad=0; worst=None
    for i in range(len(names)):
        for j in range(i+1,len(names)):
            a,b=P2[names[i]],P2[names[j]]
            m=max(abs(a[0]-b[0])/25.0, abs(a[1]-b[1])/250.0, abs(a[2]-b[2])/0.05)
            if m<=1.0:
                bad+=1; print(f"  TOO SIMILAR: {names[i]} vs {names[j]}")
            if worst is None or m<worst[0]: worst=(m,names[i],names[j])
    print(f"  => {len(names)} sounds, {bad} indistinguishable pair(s)")
    print(f"  tightest pair: {worst[1]} vs {worst[2]} at {worst[0]:.2f}x the threshold")
