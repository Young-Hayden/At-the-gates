using UnityEngine;

/// <summary>
/// A flying projectile (arrow). Damage is applied on arrival, so a shot that misses
/// because the target died mid-flight simply fizzles. Works on units and buildings.
/// </summary>
public class Projectile : MonoBehaviour
{
    IDamageable target;
    int damage;
    float speed = 9f;
    float life = 5f;
    float aimHeight = 0.5f;

    public static Projectile Spawn(GameObject prefab, Vector3 from, IDamageable target,
                                   int damage, float speed, Color tint)
    {
        if (prefab == null || target == null || !target.IsAlive) return null;
        var go = Instantiate(prefab, from, Quaternion.identity);
        var p = go.GetComponent<Projectile>();
        if (p == null) p = go.AddComponent<Projectile>();
        p.Init(target, damage, speed, tint);
        return p;
    }

    public void Init(IDamageable tgt, int dmg, float spd, Color tint)
    {
        target = tgt;
        damage = dmg;
        speed = Mathf.Max(1f, spd);
        // buildings are aimed at their base, units at their middle
        aimHeight = tgt is Building ? 1.2f : 0.5f;
        if (tint != Color.clear)
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>()) sr.color = tint;
        Aim();
    }

    void Update()
    {
        life -= Time.deltaTime;
        if (life <= 0f) { Destroy(gameObject); return; }
        if (Combatants.IsGone(target)) { Destroy(gameObject); return; }

        Vector3 to = AimPoint();
        transform.position = Vector3.MoveTowards(transform.position, to, speed * Time.deltaTime);
        Aim();

        if ((transform.position - to).sqrMagnitude < 0.06f)
        {
            target.TakeDamage(damage);
            if (FxBank.Instance != null) FxBank.Instance.Hit(to);
            Destroy(gameObject);
        }
    }

    Vector3 AimPoint()
    {
        if (Combatants.IsGone(target)) return transform.position;
        return target.Transform.position + Vector3.up * aimHeight;
    }

    void Aim()
    {
        Vector3 dir = AimPoint() - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
    }
}
