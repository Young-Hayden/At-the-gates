using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>JSON root for one manual save slot. Only long-lived match state is stored.</summary>
[Serializable]
public class SaveGameData
{
    public int version = SaveGameService.Version;
    public string savedAtUtc;
    public string sceneName;
    public EconomySave economy = new EconomySave();
    public GameState.SaveState gameState = new GameState.SaveState();
    public List<AIController.SaveState> ai = new List<AIController.SaveState>();
    public List<SaveObjectRecord> buildings = new List<SaveObjectRecord>();
    public List<SaveUnitRecord> units = new List<SaveUnitRecord>();
    public List<SaveBushRecord> bushes = new List<SaveBushRecord>();
    public SaveCamera camera = new SaveCamera();
}

[Serializable]
public class EconomySave
{
    public int gold;
    public int wood;
    public int meat;
}

[Serializable]
public class SaveObjectRecord
{
    public string prefabId;
    public string objectName;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 localScale = Vector3.one;
    public Building.SaveState building = new Building.SaveState();
    public bool hasProduction;
    public ProductionBuilding.SaveState production = new ProductionBuilding.SaveState();
    public List<ResourceNode.SaveState> resources = new List<ResourceNode.SaveState>();
    public bool hasGarrison;
    public SaveGarrisonRecord garrison;
}

[Serializable]
public class SaveGarrisonRecord
{
    public string prefabId;
    public UnitActor.SaveState actor = new UnitActor.SaveState();
}

[Serializable]
public class SaveUnitRecord
{
    public string prefabId;
    public string objectName;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 localScale = Vector3.one;
    public UnitActor.SaveState actor = new UnitActor.SaveState();
    public bool hasGatherer;
    public Gatherer.SaveState gatherer = new Gatherer.SaveState();
}

[Serializable]
public class SaveBushRecord
{
    public string prefabId;
    public string objectName;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 localScale = Vector3.one;
}

[Serializable]
public class SaveCamera
{
    public Vector3 position;
    public float orthographicSize;
    public bool valid;
}
