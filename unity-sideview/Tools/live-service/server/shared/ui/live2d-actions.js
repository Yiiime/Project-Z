// Model-specific authored library. Candidates and raw samples are not automatic choices.
export const HARU_ACTIONS = {
  "none": {
    "label": "自然待机",
    "kind": "none",
    "durationMs": 0,
    "category": "基础",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "sampleM26": {
    "label": "原始样例 m26",
    "group": "TapBody",
    "index": 0,
    "durationMs": 4970,
    "kind": "clip",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  },
  "sampleM06": {
    "label": "原始样例 m06",
    "group": "TapBody",
    "index": 1,
    "durationMs": 4530,
    "kind": "clip",
    "armSet": "B",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  },
  "openArms": {
    "label": "双手展开",
    "kind": "arms",
    "armSet": "B",
    "durationMs": 3100,
    "arms": {
      "ParamArmLB": 5,
      "ParamArmRB": 5,
      "ParamHandChangeR": 0.5,
      "ParamHandDhangeL": 0,
      "ParamHandAngleR": 0.2,
      "ParamHandAngleL": -0.2
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    }
  },
  "coverMouth": {
    "label": "害羞掩嘴",
    "kind": "arms",
    "armSet": "B",
    "durationMs": 3100,
    "arms": {
      "ParamArmLB": 3,
      "ParamArmRB": 0,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 0,
      "ParamHandAngleR": -0.1
    },
    "head": {
      "ParamAngleX": -5,
      "ParamAngleY": -18,
      "ParamAngleZ": 0
    },
    "notes": "手背朝外的弯指手型；头部与手位置一起标定，不自由转头。",
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    }
  },
  "present": {
    "label": "单手摊掌",
    "kind": "arms",
    "armSet": "B",
    "durationMs": 3100,
    "arms": {
      "ParamArmLB": 0,
      "ParamArmRB": 5,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 0,
      "ParamHandAngleL": -0.3
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    }
  },
  "handWave": {
    "label": "自然挥手（两次）",
    "kind": "arms",
    "armSet": "B",
    "durationMs": 3100,
    "wristWave": false,
    "arms": {
      "ParamArmLB": 0,
      "ParamArmRB": 4.65,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 1,
      "ParamHandAngleL": 0.1
    },
    "waveCycles": 2,
    "notes": "举掌后以手臂小幅摆动两次，手腕保持稳定；组合动作，不冒充原生 motion。",
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    }
  },
  "chin": {
    "label": "抬手思考",
    "kind": "arms",
    "armSet": "B",
    "durationMs": 3100,
    "arms": {
      "ParamArmLB": 3,
      "ParamArmRB": 0,
      "ParamHandChangeR": 0.5,
      "ParamHandDhangeL": 0,
      "ParamHandAngleR": 0.4
    },
    "head": {
      "ParamAngleX": -3,
      "ParamAngleY": -3,
      "ParamAngleZ": -7
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    }
  },
  "nod": {
    "label": "轻点头",
    "kind": "nod",
    "durationMs": 2000,
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "shake": {
    "label": "轻摇头",
    "kind": "shake",
    "durationMs": 2400,
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "tilt": {
    "label": "侧头思考",
    "kind": "tilt",
    "durationMs": 2800,
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "lean": {
    "label": "倾身倾听",
    "kind": "lean",
    "durationMs": 3000,
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "emphasis": {
    "label": "轻微强调",
    "kind": "emphasis",
    "durationMs": 1900,
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "draw": {
    "label": "低头看牌（替代表现）",
    "kind": "lookDown",
    "durationMs": 3000,
    "fallbackFor": "draw-card",
    "category": "头身小动作",
    "review": "calibrated",
    "automatic": true,
    "priority": 1
  },
  "wavePose": {
    "label": "举掌招呼（静态）",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 0,
      "ParamArmRB": 4.65,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 1,
      "ParamHandAngleL": 0.1
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "presentRight": {
    "label": "另一侧摊掌",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 5,
      "ParamArmRB": 0,
      "ParamHandChangeR": 0.5,
      "ParamHandDhangeL": 0,
      "ParamHandAngleR": 0.2
    },
    "review": "candidate",
    "automatic": false,
    "category": "手部与姿势",
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "foldArms": {
    "label": "抱臂停留",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": -1,
      "ParamArmRB": -1,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 0
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "relaxArms": {
    "label": "双臂放下",
    "kind": "arms",
    "armSet": "A",
    "arms": {
      "ParamArmLA": -1,
      "ParamArmRA": -1
    },
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "cheek": {
    "label": "脸侧抬手",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 3.5,
      "ParamArmRB": 0,
      "ParamHandChangeR": 0.5,
      "ParamHandDhangeL": 0
    },
    "review": "candidate",
    "automatic": false,
    "notes": "不承诺与脸颊接触，需要按头姿继续校准。",
    "category": "手部与姿势",
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "shrug": {
    "label": "摊手疑问",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 5,
      "ParamArmRB": 5,
      "ParamHandChangeR": 0.5,
      "ParamHandDhangeL": 0,
      "ParamHandAngleR": 0.2,
      "ParamHandAngleL": -0.2
    },
    "head": {
      "ParamAngleZ": -10,
      "ParamAngleY": 3
    },
    "review": "candidate",
    "automatic": false,
    "category": "手部与姿势",
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "startle": {
    "label": "突然惊讶",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 4.8,
      "ParamArmRB": 4.8,
      "ParamHandChangeR": 1,
      "ParamHandDhangeL": 1
    },
    "priority": 2,
    "timing": {
      "enterMs": 180,
      "holdMs": 950,
      "exitMs": 650
    },
    "head": {
      "ParamAngleY": 7,
      "ParamBodyAngleY": -3
    },
    "defaultMood": "surprised",
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "durationMs": 1780
  },
  "discover": {
    "label": "惊喜发现",
    "kind": "arms",
    "armSet": "B",
    "arms": {
      "ParamArmLB": 0,
      "ParamArmRB": 4.9,
      "ParamHandChangeR": 0,
      "ParamHandDhangeL": 0
    },
    "priority": 2,
    "timing": {
      "enterMs": 180,
      "holdMs": 1200,
      "exitMs": 650
    },
    "head": {
      "ParamAngleX": -15,
      "ParamAngleY": 6,
      "ParamAngleZ": -5,
      "ParamEyeBallX": -0.6
    },
    "face": {
      "eyeOpen": 1.7,
      "eyeSmile": 0,
      "brow": 0.85,
      "pupil": -0.6,
      "mouthForm": 0.6
    },
    "faceMs": 500,
    "defaultMood": "happy",
    "notes": "先快速看向一侧并抬手，随即转为开心；没有手指指向绑定，使用摊掌示意。",
    "category": "手部与姿势",
    "review": "calibrated",
    "automatic": true,
    "durationMs": 2030
  },
  "lookLeft": {
    "label": "转头看一侧",
    "kind": "arms",
    "armSet": "A",
    "arms": {},
    "head": {
      "ParamAngleX": -20,
      "ParamEyeBallX": -0.65
    },
    "review": "candidate",
    "automatic": false,
    "category": "手部与姿势",
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "lookRight": {
    "label": "转头看另一侧",
    "kind": "arms",
    "armSet": "A",
    "arms": {},
    "head": {
      "ParamAngleX": 20,
      "ParamEyeBallX": 0.65
    },
    "review": "candidate",
    "automatic": false,
    "category": "手部与姿势",
    "priority": 1,
    "timing": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650
    },
    "durationMs": 3100
  },
  "sampleIdle": {
    "label": "原始样例 Idle",
    "kind": "clip",
    "group": "Idle",
    "index": 0,
    "durationMs": 10000,
    "armSet": "A",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  },
  "sampleM15": {
    "label": "原始样例 M15",
    "kind": "clip",
    "group": "Idle",
    "index": 1,
    "durationMs": 5330,
    "armSet": "B",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  },
  "sampleM20": {
    "label": "原始样例 M20",
    "kind": "clip",
    "group": "TapBody",
    "index": 2,
    "durationMs": 6030,
    "armSet": "B",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  },
  "sampleM09": {
    "label": "原始样例 M09",
    "kind": "clip",
    "group": "TapBody",
    "index": 3,
    "durationMs": 4030,
    "armSet": "A",
    "category": "原始样例",
    "automatic": false,
    "review": "source",
    "priority": 1
  }
};
export const HARU_ACTION_LIBRARY = {
  "version": 1,
  "model": "Haru",
  "scope": "现有绑定的动作库，不代表穷尽连续参数组合",
  "parameterLimits": {
    "ParamArmLA": [
      -1,
      1
    ],
    "ParamArmRA": [
      -1,
      1
    ],
    "ParamArmLB": [
      -1,
      5
    ],
    "ParamArmRB": [
      -1,
      5
    ],
    "ParamHandChangeR": [
      0,
      0.5,
      1
    ],
    "ParamHandDhangeL": [
      0,
      0.5,
      1
    ],
    "ParamHandAngleR": [
      -1,
      1
    ],
    "ParamHandAngleL": [
      -1,
      1
    ]
  },
  "handShapes": {
    "0": "弯指手型（右手用于掩嘴）",
    "1": "张掌手型（朝观众，可用于招呼）",
    "0.5": "另一种侧向手型"
  },
  "transitionPolicies": {
    "smooth": {
      "enterMs": 550,
      "holdMs": 1900,
      "exitMs": 650,
      "description": "从当前姿势接续，停留后收回"
    },
    "sudden": {
      "enterMs": 180,
      "holdMs": 1200,
      "exitMs": 650,
      "description": "立即打断普通动作、先出反应再说话；短过渡快速到位"
    }
  },
  "limitations": [
    "手型是离散图，不是手指骨骼",
    "不能将张掌图镜像当作手背",
    "不同头姿的手脸接触需单独标定",
    "不存在道具或任意全身动作绑定"
  ],
  "calibrationSamples": [
    {
      "id": "B-L--1-0",
      "side": "L",
      "position": -1,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L--1-0.5",
      "side": "L",
      "position": -1,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L--1-1",
      "side": "L",
      "position": -1,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-0-0",
      "side": "L",
      "position": 0,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-0-0.5",
      "side": "L",
      "position": 0,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-0-1",
      "side": "L",
      "position": 0,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-1-0",
      "side": "L",
      "position": 1,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-1-0.5",
      "side": "L",
      "position": 1,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-1-1",
      "side": "L",
      "position": 1,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-2-0",
      "side": "L",
      "position": 2,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-2-0.5",
      "side": "L",
      "position": 2,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-2-1",
      "side": "L",
      "position": 2,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-3-0",
      "side": "L",
      "position": 3,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-3-0.5",
      "side": "L",
      "position": 3,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-3-1",
      "side": "L",
      "position": 3,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-4-0",
      "side": "L",
      "position": 4,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-4-0.5",
      "side": "L",
      "position": 4,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-4-1",
      "side": "L",
      "position": 4,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-5-0",
      "side": "L",
      "position": 5,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-5-0.5",
      "side": "L",
      "position": 5,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-L-5-1",
      "side": "L",
      "position": 5,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R--1-0",
      "side": "R",
      "position": -1,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R--1-0.5",
      "side": "R",
      "position": -1,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R--1-1",
      "side": "R",
      "position": -1,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-0-0",
      "side": "R",
      "position": 0,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-0-0.5",
      "side": "R",
      "position": 0,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-0-1",
      "side": "R",
      "position": 0,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-1-0",
      "side": "R",
      "position": 1,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-1-0.5",
      "side": "R",
      "position": 1,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-1-1",
      "side": "R",
      "position": 1,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-2-0",
      "side": "R",
      "position": 2,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-2-0.5",
      "side": "R",
      "position": 2,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-2-1",
      "side": "R",
      "position": 2,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-3-0",
      "side": "R",
      "position": 3,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-3-0.5",
      "side": "R",
      "position": 3,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-3-1",
      "side": "R",
      "position": 3,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-4-0",
      "side": "R",
      "position": 4,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-4-0.5",
      "side": "R",
      "position": 4,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-4-1",
      "side": "R",
      "position": 4,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-5-0",
      "side": "R",
      "position": 5,
      "hand": 0,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-5-0.5",
      "side": "R",
      "position": 5,
      "hand": 0.5,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    },
    {
      "id": "B-R-5-1",
      "side": "R",
      "position": 5,
      "hand": 1,
      "status": "unreviewed",
      "purpose": "绑定校准采样，不是可自动播放的动作"
    }
  ]
};
