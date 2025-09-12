using UnityEngine;

public class Pachy : Creature
{
	public Transform Spine0,Spine1,Spine2,Neck0,Neck1,Neck2,Tail2,Tail3,Tail4,Tail5,Tail6,Left_Hips,Right_Hips,Left_Leg,Right_Leg,Left_Foot0,Right_Foot0;
  public AudioClip Waterflush,Hit_jaw,Hit_head,Hit_tail,Medstep,Medsplash,Sniff2,Chew,Idleherb,Pachy1,Pachy2,Pachy3,Pachy4;

	//*************************************************************************************************************************************************
	//Play sound
	void OnCollisionStay(Collision col)
	{
		int rndPainsnd=Random.Range(0, 4); AudioClip painSnd=null;
		switch (rndPainsnd) { case 0: painSnd=Pachy1; break; case 1: painSnd=Pachy2; break; case 2: painSnd=Pachy3; break; case 3: painSnd=Pachy4; break; }
		ManageCollision(col, source, painSnd, Hit_jaw, Hit_head, Hit_tail);
	}
	void PlaySound(string name, int time)
	{
		if(time==currframe && lastframe!=currframe)
		{
			switch (name)
			{
			case "Step": source[1].pitch=Random.Range(0.75f, 1.25f); 
				if(isInWater) source[1].PlayOneShot(Waterflush, Random.Range(0.25f, 0.5f));
				else if(isOnWater) source[1].PlayOneShot(Medsplash, Random.Range(0.25f, 0.5f));
				else if(isOnGround) source[1].PlayOneShot(Medstep, Random.Range(0.25f, 0.5f));
				lastframe=currframe; break;
			case "Die": source[1].pitch=Random.Range(1.0f, 1.25f); source[1].PlayOneShot(isOnWater|isInWater?Medsplash:Medstep, 1.0f);
				lastframe=currframe; isDead=true; break;
			case "Chew": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Chew, 0.5f);
				lastframe=currframe; break;
			case "Sniff": source[0].pitch=Random.Range(1.25f, 1.5f); source[0].PlayOneShot(Sniff2, 0.5f);
				lastframe=currframe; break;
			case "Repose": source[0].pitch=Random.Range(1.0f, 1.25f); source[0].PlayOneShot(Idleherb, 0.25f);
				lastframe=currframe; break;
			case "Atk": int rnd1=Random.Range (0, 2); source[0].pitch=Random.Range(1.0f, 1.25f);
				if(rnd1==0)source[0].PlayOneShot(Pachy2, 1.0f);
				else source[0].PlayOneShot(Pachy4, 1.0f);
				lastframe=currframe; break;
			case "Growl": int rnd2=Random.Range (0, 2); source[0].pitch=Random.Range(1.0f, 1.25f);
				if(rnd2==0)source[0].PlayOneShot(Pachy1, 1.0f);
				else source[0].PlayOneShot(Pachy3, 1.0f);
				lastframe=currframe; break;
			}
		}
	}

	//*************************************************************************************************************************************************
	// Add forces to the Rigidbody
	void FixedUpdate ()
	{
		StatusUpdate(); if(!isActive | animSpeed==0.0f) { body.Sleep(); return; }
		onReset=false; onAttack=false; isConstrained= false;

		if(useAI && health!=0) { AICore(1, 2, 3, 0, 4, 5, 6); }// CPU
		else if(health!=0) { GetUserInputs(1, 2, 3, 0, 4, 5, 6); }// Human
		else { anm.SetBool("Attack", false); anm.SetInteger ("Move", 0); anm.SetInteger ("Idle", -1); }//Dead

    //Set Y position
    if(isOnGround | isInWater | isOnWater)
    {
      if(!isOnGround && !isInWater) { body.linearDamping=1; body.angularDamping=1; } else { body.linearDamping=4; body.angularDamping=4; }
      ApplyYPos();
    } else ApplyGravity();

		//Stopped
		if(OnAnm.IsName("Pachy|IdleA") | OnAnm.IsName("Pachy|Die"))
		{
       Move(Vector3.zero);
			if(OnAnm.IsName("Pachy|Die")) { onReset=true; if(!isDead) { PlaySound("Atk", 1); PlaySound("Die", 12); } }
		}

		//Forward
		else if(OnAnm.IsName("Pachy|Walk") | OnAnm.IsName("Pachy|WalkGrowl"))
		{
			Move(transform.forward, 48);
			if(OnAnm.IsName("Pachy|WalkGrowl")) { PlaySound("Growl", 1); PlaySound("Step", 6); PlaySound("Step", 13); }
			else { PlaySound("Step", 6); PlaySound("Step", 13); }
		}

		//Running
		else if(OnAnm.IsName("Pachy|Run") | OnAnm.IsName("Pachy|RunGrowl") |
		   (OnAnm.IsName("Pachy|RunAtk") && OnAnm.normalizedTime < 0.6))
		{
      roll=Mathf.Clamp(Mathf.Lerp(roll, spineX*10.0f, 0.05f), -30f, 30f);
			Move(transform.forward, 128);
			PlaySound("Step", 4); PlaySound("Step",12); 
			if(OnAnm.IsName("Pachy|RunGrowl")) PlaySound("Growl", 1);
			else if(OnAnm.IsName("Pachy|RunAtk")) { onAttack=true; PlaySound("Atk", 3); }
		}

		//Backward
		else if(OnAnm.IsName("Pachy|Walk-") | OnAnm.IsName("Pachy|WalkGrowl-"))
		{	
			Move(-transform.forward, 40);
			PlaySound("Step", 4); PlaySound("Step",12); 
			if(OnAnm.IsName("Pachy|WalkGrowl-")) PlaySound("Growl", 1);
		}

		//Strafe/Turn right
		else if(OnAnm.IsName("Pachy|Strafe-"))
		{
			Move(transform.right, 20);
			PlaySound("Step", 4); PlaySound("Step", 10);
		}

		//Strafe/Turn left
		else if(OnAnm.IsName("Pachy|Strafe+"))
		{
			Move(-transform.right, 20);
			PlaySound("Step", 4); PlaySound("Step", 10);
		}

    //Idle Attack
    else if(OnAnm.IsName("Pachy|IdleAtk"))
    { 
      Move(Vector3.zero);
      onAttack=true; PlaySound("Atk", 2); PlaySound("Step", 3);
    }

		//Various
		else if(OnAnm.IsName("Pachy|EatA")) PlaySound("Chew", 10);
		else if(OnAnm.IsName("Pachy|EatB")) { PlaySound("Chew", 1); PlaySound("Chew", 4); PlaySound("Chew", 8); PlaySound("Chew", 12); }
		else if(OnAnm.IsName("Pachy|EatC")) onReset=true;
		else if(OnAnm.IsName("Pachy|ToSleep")) { onReset=true; isConstrained=true; }
		else if(OnAnm.IsName("Pachy|Sleep")) { onReset=true; isConstrained=true; PlaySound("Repose", 2); }
		else if(OnAnm.IsName("Pachy|ToSleep-")) { isConstrained=true; PlaySound("Sniff", 1); }
		else if(OnAnm.IsName("Pachy|IdleB")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Pachy|IdleC")) { onReset=true; PlaySound("Sniff", 1); }
		else if(OnAnm.IsName("Pachy|IdleD")) PlaySound("Growl", 2);
		else if(OnAnm.IsName("Pachy|Die-")) { isConstrained=true; PlaySound("Growl", 3); isDead=false;  }

    RotateBone(IkType.LgBiped, 65f);
	}

  //*************************************************************************************************************************************************
	// Bone rotation
	void LateUpdate()
	{
		if(!isActive) return; headPos=Head.GetChild(0).GetChild(0).position;
		float headZ =-headY*headX/yaw_Max;
		Spine0.rotation*= Quaternion.Euler(-headY, 0, headX);
		Spine1.rotation*= Quaternion.Euler(-headY, 0, headX);
		Spine2.rotation*= Quaternion.Euler(-headY, 0, headX);
		Neck0.rotation*= Quaternion.Euler(-headY, headZ, headX);
		Neck1.rotation*= Quaternion.Euler(-headY, headZ, headX);
		Neck2.rotation*= Quaternion.Euler(-headY, headZ, headX);
		Head.rotation*= Quaternion.Euler(-headY, headZ, headX);
		Tail2.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail3.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail4.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail5.rotation*= Quaternion.Euler(0, 0, -spineX);
		Tail6.rotation*= Quaternion.Euler(0, 0, -spineX);
		Right_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
		Left_Hips.rotation*= Quaternion.Euler(-roll, 0, 0);
    if(!isDead) Head.GetChild(0).transform.rotation*=Quaternion.Euler(-lastHit, 0, 0);
		//Check for ground layer
		GetGroundPos(IkType.LgBiped, Right_Hips, Right_Leg, Right_Foot0, Left_Hips, Left_Leg, Left_Foot0);
	}
}
